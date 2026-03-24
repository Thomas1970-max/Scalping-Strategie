using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static System.ComponentModel.TypeConverter;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{

    public class EnumDescriptionTypeConverter : EnumConverter
    {
        private Type _enumType;

      
    public EnumDescriptionTypeConverter() : base(typeof(object))
        {
        }

        public EnumDescriptionTypeConverter(Type type) : base(type)
        {
            _enumType = type;
        }

        private Type ResolveEnumType(ITypeDescriptorContext context)
        {
            if (_enumType != null) return _enumType;
            if (context?.PropertyDescriptor?.PropertyType != null && context.PropertyDescriptor.PropertyType.IsEnum)
            {
                _enumType = context.PropertyDescriptor.PropertyType;
                return _enumType;
            }
            return _enumType ?? typeof(object);
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            var enumType = ResolveEnumType(context);
            var values = Enum.GetValues(enumType).Cast<object>().ToArray();
            return new StandardValuesCollection(values);
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            var enumType = ResolveEnumType(context);
            if (destinationType == typeof(string) && value != null)
            {
                var fi = enumType.GetField(value.ToString());
                if (fi != null)
                {
                    var da = fi.GetCustomAttribute<DescriptionAttribute>(false);
                    if (da != null)
                        return da.Description;
                }
                return value.ToString();
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var enumType = ResolveEnumType(context);
            if (value is string s)
            {
                foreach (var fi in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var da = fi.GetCustomAttribute<DescriptionAttribute>(false);
                    if (da != null && string.Equals(da.Description, s, StringComparison.CurrentCultureIgnoreCase))
                        return Enum.Parse(enumType, fi.Name);
                }
                if (Enum.TryParse(enumType, s, true, out var parsed))
                    return parsed;
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}


