﻿using System;
using System.ComponentModel;
using System.Globalization;

namespace TodoBackendTemplate
{
    /// ClientId strongly typed id
    // To support model binding using aspnetcore 2 FromHeader
    [TypeConverter(typeof(ClientIdStringConverter))]
    public class ClientId
    {
        private ClientId(Guid value) => Value = value;

        public Guid Value { get; }

        // NB for validation [and XSS] purposes we must prove it translatable to a Guid
        public static ClientId Parse(string input) => new ClientId(Guid.Parse(input));

        public override string ToString() => Value.ToString("N");
    }

    class ClientIdStringConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) =>
            value is string s ? ClientId.Parse(s) : base.ConvertFrom(context, culture, value);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
            Type destinationType) =>
            value is ClientId c && destinationType == typeof(string)
                ? c.Value
                : base.ConvertTo(context, culture, value, destinationType);
    }
}