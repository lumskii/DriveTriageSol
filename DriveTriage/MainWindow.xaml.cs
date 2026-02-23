using System.Windows;
using DriveTriage.ViewModels;

namespace DriveTriage
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
