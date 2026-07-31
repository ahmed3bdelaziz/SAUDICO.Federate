using System;
using System.Globalization;
using System.Windows.Data;
using SAUDICO.Federate.ACC.DataManagement;

namespace SAUDICO.Federate.UI;

/// <summary>Maps an <see cref="AccNodeKind"/> to the friendly "Type" column label.</summary>
public sealed class AccNodeKindToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AccNodeKind.Hub => "Hub",
        AccNodeKind.Project => "Project",
        AccNodeKind.Folder => "Folder",
        AccNodeKind.RvtFile => "RVT File",
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
