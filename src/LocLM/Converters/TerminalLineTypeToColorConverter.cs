using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LocLM.ViewModels;

namespace LocLM.Converters;

public class TerminalLineTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TerminalLineType type)
        {
            return type switch
            {
                TerminalLineType.Command => new SolidColorBrush(Color.Parse("#8EA4FF")),
                TerminalLineType.Output => new SolidColorBrush(Color.Parse("#E8ECF5")),
                TerminalLineType.Error => new SolidColorBrush(Color.Parse("#fda4af")),
                _ => new SolidColorBrush(Color.Parse("#A7AFC1"))
            };
        }
        return new SolidColorBrush(Color.Parse("#A7AFC1"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
