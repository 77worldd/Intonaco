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
            // Ensure no chrome leaves black bars during resize to desktop.
            Loaded += (_, _) =>
            {
                ContentGrid.Margin = new Thickness(0);
                ContentGrid.Background = Brushes.Black;
                Background = Brushes.Black;
            };
        }

        public Grid PlayerGrid => ContentGrid;
    }
}
