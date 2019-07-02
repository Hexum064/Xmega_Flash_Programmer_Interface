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
using System.Timers;
using System.Windows.Threading;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;

namespace Xmega_Flash_Programmer_Interface
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int BAUD_RATE = 115200;
        private const int DATA_BITS = 8;
        private const string TITLE_BASE = "Flash Programmer UI ";
        private const int COM_POLL_INTERVAL = 500;
        private const int ACTION_TIMEOUT = 5000;
        private const int MAX_MESSAGE_CHARS = 4092; //4095 - 2 bytes for len and 1 byte for null terminator

        private const byte RX_WRITE_TEXT = 0x10;
        private const byte RX_WRITE_DATA = 0x20;
        private const byte RX_ERASE_ALL = 0x30;
        private const byte RX_READ_ID = 0x01;
        private const byte RX_READ_TEXT = 0x11;
        private const byte RX_READ_DATA = 0x21;


        private Timer _comPortsTimer = new Timer(COM_POLL_INTERVAL);        
        private SerialPort _port = null;
        private Dispatcher _dispatcher = null;
        private byte _lastCmd = 0;
        private int _bytesRead = 0;
        private int _dataLength = 0;
        private List<byte> _data = new List<byte>();

        public MainWindow()
        {

            InitializeComponent();
            Files = new ObservableCollection<MidiFileModel>();
            DataContext = this;
            _comPortsTimer.Elapsed += _comPortsTimer_Elapsed;
            Loaded += (s, e) => Initialze();
        }

        public ObservableCollection<MidiFileModel> Files { get; private set; }

        private void Initialze()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            LoadComPorts(SerialPort.GetPortNames().ToList());
            //_comPortsTimer.Enabled = true;
            CharsLeftRun.Text = MAX_MESSAGE_CHARS.ToString();
        }

        private void LoadComPorts(List<string> ports)
        {
            string port = ComPortsCombo.Text;
            ComPortsCombo.Items.Clear();


            ports.ForEach((p) => ComPortsCombo.Items.Add(p));

            if (!string.IsNullOrEmpty(port) && ports.Contains(port))
            {
                ComPortsCombo.Text = port;
            }
        }

        private void ConnectToComPort(string portName)
        {
            try
            {
                if (string.IsNullOrEmpty(portName))
                {
                    return;
                }

                //Make sure we are disconnected from the current port
                DisconnectFromPort();

                _port = new SerialPort(portName, BAUD_RATE, Parity.None, DATA_BITS, StopBits.One);
                _port.Open();

                _port.DataReceived += _port_DataReceived;

                Title = $"{TITLE_BASE} (Connected To {_port.PortName})";
            }
            catch (Exception exc)
            {
                MessageBox.Show($"Exception while closing com port. \n{exc}");
            }
        }

        private void DisconnectFromPort()
        {
            try
            {
                if (!IsConnected)
                {
                    Title = $"{TITLE_BASE} (Disconnected)";
                    return;
                }

                _port.Close();
                _port = null;

                Title = $"{TITLE_BASE} (Disconnected)";
            }
            catch (Exception exc)
            {
                MessageBox.Show($"Exception while closing com port. \n{exc}");
            }
        }

        private void _port_PinChanged(object sender, SerialPinChangedEventArgs e)
        {
            Console.WriteLine(_port.ReadExisting());
        }

        private bool IsConnected
        {
            get { return _port != null && _port.IsOpen; }
        }

        #region Events


        private void _port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            byte[] bytes;

            try
            {
                switch (_lastCmd)
                {
                    case RX_READ_ID:

                        byte[] data = new byte[3];

                        _port.Read(data, 0, 3);

                        _dispatcher.Invoke(() =>
                        {
                            IdRun.Text = string.Format("{0:x2}", data[0]);
                            MemTypeRun.Text = string.Format("{0:x2}", data[1]);
                            MemSizeRun.Text = string.Format("{0:x2}", data[2]);
                        });
                        _lastCmd = 0;
                        break;
                    case RX_ERASE_ALL:
                        _port.ReadExisting(); //clear the receive buff
                        _lastCmd = 0;
                        break;
                    case RX_WRITE_TEXT:
                        //clear the receive buff
                        Debug.WriteLine(_port.ReadExisting());
                        _lastCmd = 0;
                        break;
                    case RX_WRITE_DATA:
                        _port.ReadExisting(); //clear the receive buff
                        _lastCmd = 0;
                        break;
                    case RX_READ_DATA:
                        break;
                    case RX_READ_TEXT:
                        bytes = new byte[_port.BytesToRead];
                        _bytesRead += bytes.Length;
                        // Debug.WriteLine(_port.ReadExisting());

                        _port.Read(bytes, 0, bytes.Length);

                        if (_bytesRead > 1 && _dataLength == 0)
                        {
                            _dataLength = (bytes[0] << 8) + bytes[1];
                            Debug.WriteLine($"Text Len: {_dataLength}");



                        }
                        else
                        {
                            _data.AddRange(bytes);
                        }

                        if (_dataLength + 2 <= _bytesRead)
                        {

                            _dispatcher.Invoke(() => MessageTextBox.Text = System.Text.Encoding.UTF8.GetString(_data.ToArray(), 0, _data.Count));
                            _lastCmd = 0;
                            Debug.WriteLine("Text Read");
                        }

                        break;
                }

            }
            catch (Exception exc)
            {
                MessageBox.Show($"Exception during last command. \n{exc}");
            }
           // _lastCmd = 0;
        }

        private void _comPortsTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _comPortsTimer.Enabled = false;

            _dispatcher.Invoke(() =>
            {
                try
                {
                    List<string> ports = SerialPort.GetPortNames().ToList();
                    LoadComPorts(ports);

                    if (!string.IsNullOrEmpty(ComPortsCombo.Text) && !ports.Contains(ComPortsCombo.Text))
                    {
                        ComPortsCombo.Text = string.Empty;
                    }

                    if (IsConnected && !ports.Contains(_port.PortName))
                    {
                        DisconnectFromPort();
                    }
                }
                catch (Exception exc)
                {
                    MessageBox.Show($"Exception while scanning com ports. Please relaunch the program \n{exc}");
                }
            });

            _comPortsTimer.Enabled = true;

        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectToComPort(ComPortsCombo.Text);

            if (IsConnected)
            {
                ClearBuffers();
                _lastCmd = RX_READ_ID;
                _port.Write(new byte[] { _lastCmd }, 0, 1);
            }
        }

        private void ReadTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }

            ClearBuffers();
            _data.Clear();
            _dataLength = 0;
            _bytesRead = 0;
            _lastCmd = RX_READ_TEXT;
            _port.Write(new byte[] { _lastCmd }, 0, 1);
        }

        private void WriteTextButton_Click(object sender, RoutedEventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            List<byte> data = new List<byte>();

            if (!IsConnected)
            {
                return;
            }

            ClearBuffers();

            _lastCmd = RX_WRITE_TEXT;

            data.Add(_lastCmd);


            data.AddRange(Get16BitBytes((uint)MessageTextBox.Text.Length));



            data.AddRange(Encoding.UTF8.GetBytes(MessageTextBox.Text));

            _port.Write(data.ToArray(), 0, data.Count);
   
            //spin wait

            sw.Start();

            while (_lastCmd != 0 && sw.ElapsedMilliseconds < ACTION_TIMEOUT) { }

            sw.Stop();

            if (sw.ElapsedMilliseconds >= ACTION_TIMEOUT)
            {
                MessageBox.Show("Time-out while attempting to write message.");
            }
            else
            {
                MessageBox.Show("Done writing message.");
            }
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }
        }

        private void ReadFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }
        }

        private void WriteFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }
        }

        private void EraseButton_Click(object sender, RoutedEventArgs e)
        {
            Stopwatch sw = new Stopwatch();

            if (!IsConnected)
            {
                return;
            }

            if (MessageBox.Show("Are you sure you want to erase all?", "Erase All?", MessageBoxButton.YesNo) == MessageBoxResult.No)
            {
                return;
            }

            //ClearBuffers();

            _lastCmd = RX_ERASE_ALL;
            _port.Write(new byte[] { _lastCmd }, 0, 1);

            //spin wait

            sw.Start();
    
            while (_lastCmd != 0 && sw.ElapsedMilliseconds < ACTION_TIMEOUT) { }

            sw.Stop();

            if (sw.ElapsedMilliseconds >= ACTION_TIMEOUT)
            {
                MessageBox.Show("Time-out while attempting to erase all.");
            }
            else
            {
                MessageBox.Show("Done erasing all.");
            }

        }


        private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CharsLeftRun.Text = (MAX_MESSAGE_CHARS - MessageTextBox.Text.Length).ToString();
        }



        private void MessageTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int pos;

            if ((MAX_MESSAGE_CHARS - MessageTextBox.Text.Length) == 0 && KeyIsAlphanumeric(e.Key))
            {
                e.Handled = true;
                return;
            }

            if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.Enter)
            {
                pos = MessageTextBox.SelectionStart;
                MessageTextBox.Text = MessageTextBox.Text.Insert(pos, Environment.NewLine);
                MessageTextBox.SelectionStart = pos + 1;
                return;
            }
        }

        #endregion

        private void ClearBuffers()
        {
            if (_port == null)
            {
                return;
            }

            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        private static byte[] Get16BitBytes(uint num)
        {
            byte[] data = { Convert.ToByte((num >> 8) & 0xFF), Convert.ToByte((num >> 0) & 0xFF) };
            return data;
        }

        private static bool KeyIsAlphanumeric(Key key)
        {
            int keyValue = (int)key;
            return (keyValue >= 0x30 && keyValue <= 0x39) // numbers
                 || (keyValue >= 0x41 && keyValue <= 0x5A) // letters
                 || (keyValue >= 0x60 && keyValue <= 0x69) // numpad
                 || keyValue == (int)Key.Enter;
        }

        private static byte[] Get32BitBytes(uint num)
        {
            byte[] data = { Convert.ToByte((num >> 24) & 0xFF), Convert.ToByte((num >> 16) & 0xFF), Convert.ToByte((num >> 8) & 0xFF), Convert.ToByte((num >> 0) & 0xFF) };
            return data;
        }
    }
}
