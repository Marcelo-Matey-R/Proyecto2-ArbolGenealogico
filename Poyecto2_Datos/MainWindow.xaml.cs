using System.Windows;


namespace Poyecto2_Datos
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        private void BtnAddNode_Click(object sender, RoutedEventArgs e)
        {
            var w = new AddNodeWindow();
            w.Owner = this;
            w.Show();
        }


        private void BtnMap_Click(object sender, RoutedEventArgs e)
        {
            var w = new MapWindow();
            w.Owner = this;
            w.Show();
        }


        private void BtnStats_Click(object sender, RoutedEventArgs e)
        {
            var w = new StatsWindow();
            w.Owner = this;
            w.Show();
        }
    }
}
