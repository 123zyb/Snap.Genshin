﻿using DGP.Genshin.Data.Weapon;
using System;
using System.Globalization;
using System.Windows.Data;

namespace DGP.Genshin.Controls.Converters
{
    public class WeaponMaterialTo2Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            WeaponMaterial material = (WeaponMaterial)value;
            return App.Current.FindResource(material.ToString() + "2");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
