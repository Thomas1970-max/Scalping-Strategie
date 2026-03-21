using System;
using System.ComponentModel;
using ATAS.DataFeedsCore;
using static MyNamespace.Strategies.Geldfluss3_3;
using MyNamespace.Strategies.Orderflow;
using System.ComponentModel.DataAnnotations;
using OFTParameter = OFT.Attributes.ParameterAttribute;

namespace MyNamespace.Strategies.Models
{
    public class NullableDecimalConverter : TypeConverter
    {
        private readonly DecimalConverter _inner = new DecimalConverter();
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        => _inner.CanConvertFrom(context, sourceType) || base.CanConvertFrom(context, sourceType);
        public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return null;
            return Convert.ToDecimal(_inner.ConvertFrom(context, culture, value));
        }
        public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
        {
            return _inner.ConvertTo(context, culture, value, destinationType);
        }
    }
}


