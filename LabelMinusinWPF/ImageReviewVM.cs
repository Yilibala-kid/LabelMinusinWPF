using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static LabelMinusinWPF.Modules;

namespace LabelMinusinWPF
{
    public partial class ImageReviewVM: ObservableObject
    {
        public MainViewModel LeftImageVM { get; } = new MainViewModel();
        public MainViewModel RightImageVM { get; } = new MainViewModel();
    }
}
