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
        private System.Windows.Forms.OpenFileDialog _fileDialog = new System.Windows.Forms.OpenFileDialog();

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
            _fileDialog.Filter = "MIDI files (*.mid)|*.mid|All files (*.*)|*.*";
            _fileDialog.Multiselect = true;
            _fileDialog.Title = "Select MIDI files to load to flash";
            _fileDialog.InitialDirectory = Environment.ExpandEnvironmentVariables("%userprofile%\\documents");
            _dispatcher = Dispatcher.CurrentDispatcher;
            LoadComPorts(SerialPort.GetPortNames().ToList());
            //_comPortsTimer.Enabled = true;
            CharsLeftRun.Text = MAX_MESSAGE_CHARS.ToString();
            UpdateFilesMetrics();
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

        //private void _port_PinChanged(object sender, SerialPinChangedEventArgs e)
        //{
        //    Console.WriteLine(_port.ReadExisting());
        //}

        private bool IsConnected
        {
            get { return _port != null && _port.IsOpen; }
        }

        private int GetDataSizeInBytes()
        {
            return Files.Sum((f) => f.ByteCount + 2) + (Files.Count * 4) + 2; //+ 2 in Sum is for the 16bits that hold the length of the file name, + 2 in the total is for the 16bits that hold the number of files.
        }

        private void UpdateFilesMetrics()
        {
            _dispatcher.Invoke(() =>
            {
                FileSizeRun.Text = GetDataSizeInBytes().ToString() + " bytes";
                FileCountRun.Text = Files.Count.ToString();
            });
        }

        private List<byte> BuildDataBuffer()
        {
            int size = GetDataSizeInBytes();
            List<byte> data = new List<byte>(size + 5); //+ 5 to hold the 32 bit number that holds the size of the data itself, including lookup table and eventually the command byte

            //Set the first 4 bytes to the total size
            data.AddRange(Get32BitBytes((uint)size));

            //Set the next two bytes to the number of files
            data.AddRange(Get16BitBytes((uint)Files.Count));

            //First run through will add the file sizes
            //Each calculation is a 32bit number that holds the total size of the MIDI file bytes + the file name + a 2 byte number to hold the lenght of the name
            Files
                .ToList()
                .ForEach((f) => data.AddRange(Get32BitBytes((uint)(f.ByteCount + 2)))); //+ 2 is for the 16bits that hold the length of the file name

            //Now add the file name lenght, file name, and file bytes
            Files
            .ToList()
            .ForEach((f) =>
            {
                data.AddRange(Get16BitBytes((uint)f.FileName.Length));
                data.AddRange(Encoding.UTF8.GetBytes(f.FileName));
                data.AddRange(f.FileBytes);

            });

            return data;
        }

        private List<MidiFileModel> BuildFilesCollection(byte[] data)
        {
            int count;
            int offset = 0; 
            int len;
            List<MidiFileModel> files = new List<MidiFileModel>();
            if (data == null)
            {
                return new List<MidiFileModel>();
            }

            count = (data[0] << 8) + data[1];
            offset = 2 + (4 * count); //Set the offset to the first location after the lookup table (4 bytes per entry) + 2 for the file count bytes;
            for (int i = 0; i < count; i++)
            {
                //First get the length of the file
                len = (data[(i * 4) + 2] << 24) + (data[(i * 4) + 3] << 16) + (data[(i * 4) + 4] << 8) + data[(i * 4) + 5];
                files.Add(BuildFileModelFromData(data, offset, len));
                offset += len;
            }

            return files;
        }

        private MidiFileModel BuildFileModelFromData(byte[] data, int offset, int len)
        {
            int nameLen = (data[offset] << 8) + data[offset + 1];
            string name = Encoding.UTF8.GetString(data, offset + 2, nameLen);
            return new MidiFileModel(name, data.Skip(offset + 2 + nameLen).Take(len - 2 - nameLen));
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
                        bytes = new byte[_port.BytesToRead];
                        _bytesRead += bytes.Length;
                        // Debug.WriteLine(_port.ReadExisting());

                        _port.Read(bytes, 0, bytes.Length);

                        if (_bytesRead > 3 && _dataLength == 0)
                        {
                            _dataLength = (bytes[0] << 24) + (bytes[1] << 16) + (bytes[2] << 8) + bytes[3];
                            Debug.WriteLine($"Data Len: {_dataLength}");
                            _data.Clear();


                        }
                        else
                        {
                            _data.AddRange(bytes);
                        }

                        if (_dataLength + 4 <= _bytesRead)
                        {
                            //TODO: Convert bytes to Files collection
                            _dispatcher.Invoke(() =>
                            {
                                Files.Clear();
                                BuildFilesCollection(_data.ToArray())
                                    .ForEach((f) => Files.Add(f));
                            });

                            UpdateFilesMetrics();

                            _lastCmd = 0;
                            Debug.WriteLine("Data Read");
                        }
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

                            _data.Clear();

                        }
                        else
                        {
                            _data.AddRange(bytes);
                        }

                        if (_dataLength + 2 <= _bytesRead)
                        {

                            _dispatcher.Invoke(() => MessageTextBox.Text = Encoding.UTF8.GetString(_data.ToArray(), 0, _data.Count));
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
                System.Windows.MessageBox.Show("Time-out while attempting to write message.");
            }
            else
            {
                MessageBox.Show("Done writing message.");
            }
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {

            List<string> badFiles = new List<string>();
            if (_fileDialog.ShowDialog() == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }

            _fileDialog.FileNames
                .ToList()
                .ForEach((f) =>
                {
                    MidiFileModel file = new MidiFileModel(System.IO.Path.GetFileName(f), File.ReadAllBytes(f));

                    if (file.FileBytes.Count() < 4 || !Encoding.UTF8.GetString(file.FileBytes.Take(4).ToArray(), 0, 4).Equals("MThd"))
                    {
                        badFiles.Add(f);
                    }
                    else
                    {
                        Files.Add(file);
                    }
                });

            if (badFiles.Count > 0)
            {
                MessageBox.Show($"The following files were not valid MIDI files (the file did not start with 'MThd')\n\n{string.Join("\n", badFiles)}");
            }


            UpdateFilesMetrics();

        }

        private void ReadFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }

            ClearBuffers();
            _data.Clear();
            _dataLength = 0;
            _bytesRead = 0;
            _lastCmd = RX_READ_DATA;
            _port.Write(new byte[] { _lastCmd }, 0, 1);
        }

        private void WriteFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                return;
            }

            Stopwatch sw = new Stopwatch();
            List<byte> data;

            if (!IsConnected)
            {
                return;
            }

            ClearBuffers();

            _lastCmd = RX_WRITE_DATA;

            data = BuildDataBuffer();
            data.Insert(0, _lastCmd);

            _port.Write(data.ToArray(), 0, data.Count);

            //spin wait

            sw.Start();

            while (_lastCmd != 0 && sw.ElapsedMilliseconds < ACTION_TIMEOUT) { }

            sw.Stop();

            if (sw.ElapsedMilliseconds >= ACTION_TIMEOUT * 2) //Double the timeout time because we need to wait for eraseing
            {
                System.Windows.MessageBox.Show("Time-out while attempting to write data.");
            }
            else
            {
                MessageBox.Show("Done writing data.");
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

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            MidiFileModel file = (e.Source as Button)?.DataContext as MidiFileModel;

            if (file == null)
            {
                return;
            }

            Files.Remove(file);

            UpdateFilesMetrics();
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

        private static void VisualizeFileBytes(MidiFileModel file)
        {
            int i;

            for (i = 0; i <  file.FileBytes.Count; i+=16)
            {

                for(int j = 0; j < 16 && i + j < file.FileBytes.Count; j++)
                {
                    Debug.Write(string.Format("{0:x2} ", file.FileBytes[i + j]));

                }

                Debug.Write("\t");

                for (int j = 0; j < 16 && i + j < file.FileBytes.Count; j++)
                {
                    Debug.Write(Convert.ToChar(file.FileBytes[i + j]));

                }

                Debug.WriteLine("");

            }

            Debug.WriteLine("");

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
