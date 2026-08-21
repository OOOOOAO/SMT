using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SMT.EVEData;

namespace SMT.Converters
{
    /// <summary>
    /// zKillboard row background from victim alliance vs active character standings.
    /// </summary>
    public class ZKBBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ZKillRedisQ.ZKBDataSimple zs = value as ZKillRedisQ.ZKBDataSimple;
            // default: transparent row, let column colours do the work
            Color rowCol = Color.FromArgb(0, 0, 0, 0);
            if(zs != null)
            {
                float Standing = 0.0f;

                LocalCharacter c = MainWindow.AppWindow.RegionUC.ActiveCharacter;
                if(c != null && c.ESILinked)
                {
                    if(c.AllianceID != 0 && c.AllianceID == zs.VictimAllianceID)
                    {
                        Standing = 10.0f;
                    }

                    if(c.Standings.Keys.Contains(zs.VictimAllianceID))
                    {
                        Standing = c.Standings[zs.VictimAllianceID];
                    }

                    // hostile: red tint bg
                    if(Standing == -10.0)
                    {
                        rowCol = Color.FromArgb(60, 248, 81, 73);   // DangerColor tint
                    }

                    if(Standing == -5.0)
                    {
                        rowCol = Color.FromArgb(40, 240, 136, 62);  // WarningColor tint
                    }

                    // friendly: blue tint bg
                    if(Standing == 5.0)
                    {
                        rowCol = Color.FromArgb(40, 31, 111, 235);  // AccentPrimary tint
                    }

                    if(Standing == 10.0)
                    {
                        rowCol = Color.FromArgb(60, 31, 111, 235);  // AccentPrimary stronger
                    }
                }
            }

            return new SolidColorBrush(rowCol);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// zKillboard row foreground from victim alliance vs active character standings.
    /// </summary>
    public class ZKBForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ZKillRedisQ.ZKBDataSimple zs = value as ZKillRedisQ.ZKBDataSimple;
            Color rowCol = Colors.White;
            if(zs != null)
            {
                float Standing = 0.0f;

                LocalCharacter c = MainWindow.AppWindow.RegionUC.ActiveCharacter;
                if(c != null && c.ESILinked)
                {
                    if(c.AllianceID != 0 && c.AllianceID == zs.VictimAllianceID)
                    {
                        Standing = 10.0f;
                    }

                    if(c.Standings.Keys.Contains(zs.VictimAllianceID))
                    {
                        Standing = c.Standings[zs.VictimAllianceID];
                    }

                    if(Standing == -10.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == -5.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == 5.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == 10.0)
                    {
                        rowCol = Colors.White;
                    }
                }
            }

            return new SolidColorBrush(rowCol);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
