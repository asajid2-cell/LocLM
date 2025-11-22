using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LocLM.Converters;

public class CountIsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => true,
            int i => i == 0,
            IList list => list.Count == 0,
            IEnumerable enumerable => !enumerable.GetEnumerator().MoveNext(),
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
