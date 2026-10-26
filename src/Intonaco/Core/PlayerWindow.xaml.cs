using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Intonaco.Core
{
    public partial class PlayerWindow : Window
    {
        public PlayerWindow()
        {
            InitializeComponent();
        }

        public Grid PlayerGrid => ContentGrid;
    }
}
