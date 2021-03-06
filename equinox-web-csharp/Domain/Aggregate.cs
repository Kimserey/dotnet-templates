﻿using Equinox;
using Equinox.Core;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FsCodec;

namespace TodoBackendTemplate
{
    public static class Aggregate
    {
        /// NB - these types and names reflect the actual storage formats and hence need to be versioned with care
        public abstract class Event
        {
            public class Happened : Event
            {
            }

            public class Snapshotted : Event
            {
                public new bool Happened { get; set; }
            }

            static readonly JsonNetUtf8Codec Codec = new JsonNetUtf8Codec(new JsonSerializerSettings());
            
            public static Event TryDecode(string et, byte[] json)
            {
                switch (et)
                {
                    case nameof(Happened): return Codec.Decode<Happened>(json);
                    case nameof(Snapshotted): return Codec.Decode<Snapshotted>(json);
                    default: return null;
                }
            }

            public static Tuple<string, byte[]> Encode(Event e) => Tuple.Create(e.GetType().Name, Codec.Encode(e));
            public static string For(ClientId id) => StreamNameModule.create("Aggregate", id.ToString());
        }
        public class State
        {
            public bool Happened { get; set; }

            internal State(bool happened) { Happened = happened; }

            public static readonly State Initial = new State(false);

            static void Evolve(State s, Event x)
            {
                switch (x)
                {
                    case Event.Happened e:
                        s.Happened = true;
                        break;
                    case Event.Snapshotted e:
                        s.Happened = e.Happened;
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(x), x, "invalid");
                }
            }

            public static State Fold(State origin, IEnumerable<Event> xs)
            {
                // NB Fold must not mutate the origin
                var s = new State(origin.Happened);
                foreach (var x in xs)
                    Evolve(s, x);
                return s;
            }

            public static bool IsOrigin(Event e) => e is Event.Snapshotted;
            
            public static Event Snapshot(State s) => new Event.Snapshotted {Happened = s.Happened};
        }

        /// Defines the decision process which maps from the intent of the `Command` to the `Event`s that represent that decision in the Stream 
        public abstract class Command
        {
            public class MakeItSo : Command
            {
            }

            public static IEnumerable<Event> Interpret(State s, Command x)
            {
                switch (x)
                {
                    case MakeItSo c:
                        if (!s.Happened) yield return new Event.Happened();
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(x), x, "invalid");
                }
            }
        }

        class Handler
        {
            readonly EquinoxStream<Event, State> _stream;

            public Handler(ILogger log, IStream<Event, State> stream) =>
                _stream = new EquinoxStream<Event, State>(State.Fold, log, stream);

            /// Execute `command`, syncing any events decided upon
            public Task<Unit> Execute(Command c) =>
                _stream.Execute(s => Command.Interpret(s, c));

            /// Establish the present state of the Stream, project from that as specified by `projection`
            public Task<T> Query<T>(Func<State, T> projection) =>
                _stream.Query(projection);
        }

        public class View
        {
            public bool Sorted { get; set; }
        }

        public class Service
        {
            /// Maps a ClientId to Handler for the relevant stream
            readonly Func<ClientId, Handler> _stream;

            public Service(ILogger handlerLog, Func<string, IStream<Event, State>> resolve) =>
                _stream = id => new Handler(handlerLog, resolve(Event.For(id)));

            /// Execute the specified command 
            public Task<Unit> Execute(ClientId id, Command command) =>
                _stream(id).Execute(command);

            /// Read the present state
            // TOCONSIDER: you should probably be separating this out per CQRS and reading from a denormalized/cached set of projections
            public Task<View> Read(ClientId id) => _stream(id).Query(Render);

            static View Render(State s) => new View() {Sorted = s.Happened};
        }
    }
}