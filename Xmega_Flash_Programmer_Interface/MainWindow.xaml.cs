using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Xmega_Flash_Programmer_Interface
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => Initialze();
        }



        private void Initialze()
        {
            LoadComPorts();
        }

        private void LoadComPorts()
        {

            ComPortsCombo.Items.Clear();

            SerialPort.GetPortNames()
                .ToList()
                .ForEach((p) => ComPortsCombo.Items.Add(p));
        }

    }
}
