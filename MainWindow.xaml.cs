using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Haukcode.ArtNet.Packets;
using Haukcode.ArtNet.Sockets;
using Haukcode.Sockets;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace MidiApp
{

    public partial class MainWindow : AdonisUI.Controls.AdonisWindow
    {

        public Thread m_Thread = null;
        public Thread activity_Thread = null;
        public Thread ArtNetactivity_Thread = null;
        public ArtNetSocket m_socket = null;
        public ArtNetSocket m_TXsocket = null;
        public static List<Follow_Spot> m_spots = new List<Follow_Spot>();

        public Thread mqConnection_Thread = null;

        public string resourceFileName = @"resources.json";
        public static dynamic AppResources;
        public Thread m_ResourceLoader_Thread = null;

        public ThreeD m_threeDWindow = null;

        SynchronizationContext context;

        public MainWindow()
        {
            InitializeComponent();

            //String strHostName = Dns.GetHostName();
            //IPHostEntry iphostentry = Dns.GetHostEntry(strHostName);

            //foreach (IPAddress ipaddress in iphostentry.AddressList)
            //{
            //    if (ipaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            //        ipInputMQ.Items.Add(ipaddress.ToString());

            //    if (ipaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            //        ipInputTX.Items.Add(ipaddress.ToString());

            //}

            context = SynchronizationContext.Current;

            AppResources = getAppResource();
            m_ResourceLoader_Thread = new Thread(new ThreadStart(resourceLoaderLoop));
            m_ResourceLoader_Thread.IsBackground = true;
            m_ResourceLoader_Thread.Start();

            try
            {
                ML_IPAddress = IPAddress.Parse((string)AppResources.Network.MAgicQIP);
                ARTNET_RXIPAddress = IPAddress.Parse((string)AppResources.Network.ArtNet.RXIP);
                ARTNET_RXSubNetMask = IPAddress.Parse((string)AppResources.Network.ArtNet.RXSubNetMask);
                ARTNET_RXUniverse = (int)AppResources.Network.ArtNet.Universe;
            }
            catch (Exception e)
            {
                MessageBox.Show("Cannot parse resource file\n" + e.Message, "Resource File Problem", MessageBoxButton.OK, MessageBoxImage.Stop);
                Close();
            }
        }

        IPAddress ML_IPAddress = null;
        IPAddress ARTNET_RXIPAddress = null;
        IPAddress ARTNET_RXSubNetMask = null;
        int ARTNET_RXUniverse = 0;

        public void saveAppResource()
        {
            string res = System.IO.File.ReadAllText(resourceFileName);
            System.IO.File.WriteAllText(resourceFileName + ".bak", res);

            System.IO.File.WriteAllText(resourceFileName, Newtonsoft.Json.JsonConvert.SerializeObject(AppResources));
        }

        public dynamic getAppResource()
        {
            try
            {
                var res = System.IO.File.ReadAllText(resourceFileName);
                return Newtonsoft.Json.JsonConvert.DeserializeObject(res);
            }
            catch (System.IO.FileNotFoundException)
            {
                MessageBox.Show("Cannot find resource file\n"+ resourceFileName, "File Not Found", MessageBoxButton.OK, MessageBoxImage.Stop);
                Close();
                return null;
            }
        }
        public void resourceLoaderLoop()
        {
            DateTime time = System.IO.File.GetLastWriteTime(resourceFileName);

            while (true)
            {
                try
                {
                    DateTime latestTime = System.IO.File.GetLastWriteTime(resourceFileName);

                    if (latestTime > time)
                    {
                        AppResources = getAppResource();
                        context.Post(delegate (object dummy)
                        {
                            m_threeDWindow?.UpdateModel();
                        }, null);

                        time = latestTime;
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
                catch (ThreadInterruptedException)
                {

                }
            }
        }

        Brush GreenFill = null;
        Brush RedFill = null;
        Brush WhiteFill = null;

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            byte v = Convert.ToByte(value);
            byte p = Convert.ToByte(value * (1 - saturation));
            byte q = Convert.ToByte(value * (1 - f * saturation));
            byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromArgb(255, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(255, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(255, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(255, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(255, t, p, v);
            else
                return Color.FromArgb(255, v, p, q);
        }

        Stopwatch activityTimer = Stopwatch.StartNew();

        void ActivityMonitor()
        {

            while (true)
            {
                try
                {
                    Thread.Sleep(100);
                    if (context != null)
                    {
                        context.Post(delegate (object dummy)
                        {
                            if (activityTimer.ElapsedMilliseconds > 200)
                            {
                                ActivityLED.Fill = WhiteFill;
                            }
                        }, null);
                    }
                }
                catch (ThreadInterruptedException)
                {

                }

            }
        }

        public void activity(int type)
        {
            activityTimer.Restart();
            if (context != null)
            {
                context.Post(delegate (object dummy)
                {
                    ActivityLED.Fill = GreenFill;
                }, null);
            }
        }

        Stopwatch ArtNetactivityTimer = Stopwatch.StartNew();

        void ArtNetActivityMonitor()
        {

            while (true)
            {
                try
                {
                    Thread.Sleep(100);
                    if (context != null)
                    {
                        context.Post(delegate (object dummy)
                        {
                            if (ArtNetactivityTimer.ElapsedMilliseconds > 200)
                            {
                                ArtNetActivityLED.Fill = WhiteFill;
                            }
                        }, null);
                    }
                }
                catch (ThreadInterruptedException)
                {

                }

            }
        }

        public void ArtNetactivity(int type)
        {
            ArtNetactivityTimer.Restart();
            if (context != null) {
                context.Post(delegate (object dummy)
                {
                    ArtNetActivityLED.Fill = GreenFill;
                }, null);
            }
        }

        Stopwatch connectionTimer = Stopwatch.StartNew();
        void mqConnection_Monitor()
        {

            while (true)
            {
                try
                {
                    Thread.Sleep(500);
                    if (context != null)
                    {
                        context.Post(delegate (object dummy)
                        {
                            if (connectionTimer.ElapsedMilliseconds > 1000)
                            {
                                ConnectionLED.Fill = WhiteFill;
                            }
                        }, null);
                    }
                }
                catch (ThreadInterruptedException)
                {

                }

            }
        }

        public void activityMQ(int type)
        {
            connectionTimer.Restart();
            if (context != null)
            {
                context.Post(delegate (object dummy)
                {
                    ConnectionLED.Fill = GreenFill;
                }, null);
            }
        }

                        //else if (parts[1].Equals("fspot"))
                        //{
                        //    if (parts.Length >= 2)
                        //    {
                        //        if (parts[2] == "start")
                        //        {
                        //            string ids = msg.ToString();
                        //            int viewID = -1;

                        //            if (!ids.EndsWith("/"))
                        //            {
                        //                ids = ids.Substring(ids.LastIndexOf('/') + 1);
                        //                string[] idspot = ids.Split(',');

                        //                int headId = Int32.Parse(idspot[0]);

                        //                foreach (Follow_Spot spot in m_spots)
                        //                {
                        //                    spot.IsLeadSpot = spot.Head == headId;
                        //                }
                        //                if (idspot.Length > 1)
                        //                {
                        //                    viewID = Int32.Parse(idspot[1]);
                        //                }
                        //            }

                        //            context.Post(delegate (object dummy)
                        //            {
                        //                if (m_threeDWindow == null)
                        //                {
                        //                    m_threeDWindow = new ThreeD();
                        //                    m_threeDWindow.grab();
                        //                }
                        //                else
                        //                {
                        //                    m_threeDWindow.Show();
                        //                }

                        //                if (viewID > 0)
                        //                {
                        //                    m_threeDWindow.setCameraView(viewID - 1);
                        //                }

                        //                if (leadSpot() >= 0)
                        //                {
                        //                    m_threeDWindow.Macro_moveSpot(leadSpot());
                        //                    m_threeDWindow.setActive(true);
                        //                }
                        //                else
                        //                {
                        //                    m_threeDWindow.setActive(false);
                        //                }
                        //            }, null);

                        //        }
                        //        else if (parts[2] == "stop")
                        //        {
                        //            foreach (Follow_Spot spot in m_spots)
                        //            {
                        //                spot.IsLeadSpot = false;
                        //            }

                        //            if (m_threeDWindow != null)
                        //            {
                        //                context.Post(delegate (object dummy)
                        //                {
                        //                    m_threeDWindow.setActive(false);
                        //                }, null);
                        //            }

        public void Mover(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Console.WriteLine("Mouse position" + e.GetPosition(this));
        }

        private void Window_Loaded(object source, RoutedEventArgs e)
        {

            context = SynchronizationContext.Current;

            try
            {
                GreenFill = new RadialGradientBrush(Color.FromRgb(0x1D, 0xFF, 0x1D), Color.FromRgb(0x00, 0xB9, 0x00));
                RedFill = new RadialGradientBrush(Color.FromRgb(0xFF, 0x1D, 0x1D), Color.FromRgb(0xE0, 0x00, 0x00));
                WhiteFill = new RadialGradientBrush(Color.FromRgb(0x60, 0x80, 0x60), Color.FromRgb(0x20, 0x60, 0x20));

                if (activity_Thread == null)
                {
                    activity_Thread = new Thread(new ThreadStart(ActivityMonitor));
                    activity_Thread.IsBackground = true;
                    activity_Thread.Start();
                }

                if (ArtNetactivity_Thread == null)
                {
                    ArtNetactivity_Thread = new Thread(new ThreadStart(ArtNetActivityMonitor));
                    ArtNetactivity_Thread.IsBackground = true;
                    ArtNetactivity_Thread.Start();
                }

                if (mqConnection_Thread == null)
                {
                    mqConnection_Thread = new Thread(new ThreadStart(mqConnection_Monitor));
                    mqConnection_Thread.IsBackground = true;
                    mqConnection_Thread.Start();
                }

                StartClient();

                //setupMQListener();
                //m_Thread = new Thread(new ThreadStart(ListenLoop));
                //m_Thread.IsBackground = true;
                //m_Thread.Start();

                foreach (dynamic v in AppResources.Lights)
                {
                    Follow_Spot spot = new Follow_Spot();
                    spot.Head = v.Head;
                    spot.Universe = v.Universe;
                    spot.Address = v.Address;
                    spot.IsLeadSpot = false;

                    Point3D p;
                    switch ((int)v.Bar)
                    {
                        case 0: p = new Point3D((double)v.XOffset, 0.0, (double)AppResources.Bar0Height + 0.1); break;
                        case -1: p = new Point3D((double)v.XOffset, (double)AppResources.BarAudienceOffset, (double)AppResources.BarAudienceHeight + 0.1); break;
                        case 1: p = new Point3D((double)v.XOffset, (double)AppResources.Bar1Offset, (double)AppResources.Bar1Height + 0.1); break;
                        case 2: p = new Point3D((double)v.XOffset, (double)AppResources.Bar2Offset, (double)AppResources.Bar2Height + 0.1); break;
                        default: p = new Point3D((double)v.XOffset, (double)AppResources.Bar3Offset, (double)AppResources.Bar3Height + 0.1); break;
                    }
                    spot.Location = p;

                    m_spots.Add(spot);
                }
                FollwSpot_dataGrid.ItemsSource = m_spots;

                ArtNetListner();

                m_threeDWindow = new ThreeD();
                m_threeDWindow.setActive(false);
                m_threeDWindow.grab();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error!",
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                Close();
            }

            // this.Topmost = true;
        }


        public static IEnumerable<(IPAddress Address, IPAddress NetMask)> GetAddressesFromInterfaceType(NetworkInterfaceType? interfaceType = null,
    Func<NetworkInterface, bool> predicate = null)
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.SupportsMulticast && (!interfaceType.HasValue || adapter.NetworkInterfaceType == interfaceType) &&
                    adapter.OperationalStatus == OperationalStatus.Up)
                {
                    if (predicate != null)
                        if (!predicate(adapter))
                            continue;

                    IPInterfaceProperties ipProperties = adapter.GetIPProperties();

                    foreach (var ipAddress in ipProperties.UnicastAddresses)
                    {
                        if (ipAddress.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            yield return (ipAddress.Address, ipAddress.IPv4Mask);
                    }
                }
            }
        }

        public int leadSpot()
        {
            int i = 0;
            for (i = 0; i < MainWindow.m_spots.Count; i++)
            {
                if (MainWindow.m_spots[i].IsLeadSpot)
                {
                    return i;
                }
            }

            return -1;
        }

        void ArtNet_NewPacket(object sender, NewPacketEventArgs<ArtNetPacket> e)
        {
            //Console.WriteLine($"Received ArtNet packet with OpCode: {e.Packet.OpCode} from {e.Source}");
            if (leadSpot() < 0)
            {
                ArtNetactivity(1);

                if (e.Packet.OpCode == Haukcode.ArtNet.ArtNetOpCodes.Dmx)
                {
                    ArtNetDmxPacket dmx = (ArtNetDmxPacket)e.Packet;
                    context.Post(delegate (object dummy)
                    {
                        foreach (Follow_Spot spot in m_spots)
                        {
                            if (dmx.Universe == spot.Universe)
                            {
                                spot.Pan = Math.Round(((dmx.DmxData[spot.Address - 1] * 256) + dmx.DmxData[spot.Address]) / 65535.0 * 540.0 - 270.0, 3);
                                spot.Tilt = Math.Round(((dmx.DmxData[spot.Address + 1] * 256) + dmx.DmxData[spot.Address + 2]) / 65535.0 * 270.0 - 135.0, 3);
                            }
                        }

                        //if (dmx.Universe == m_spots[0].Universe - 1)
                        //{
                        //                        Follow_Spot spot = m_spots[0];

                        //                        int p = (dmx.DmxData[spot.Address - 1] * 256) + dmx.DmxData[spot.Address];
                        //                        int t = (dmx.DmxData[spot.Address + 1] * 256) + dmx.DmxData[spot.Address + 2];

                        //Console.WriteLine("P: {0}, T:{1}", p, t);

                        //}

                        if (m_threeDWindow != null)
                        {
                            m_threeDWindow.DMX_moveSpot(leadSpot());
                        }

                    }, null);

                    //if ((leadSpot() < 0) && ( (dmx.Universe != (short)ARTNET_RXUniverse)))
                    //    updateDMX(dmx.DmxData);
                }
            }

        }
        void ArtNetListner()
        {

            m_socket = new ArtNetSocket();
            m_TXsocket = new ArtNetSocket();

            m_socket.NewPacket += ArtNet_NewPacket;

//            var addresses = GetAddressesFromInterfaceType();
//            var addr = addresses.ToArray()[2];

            m_socket.Open(ARTNET_RXIPAddress, ARTNET_RXSubNetMask);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //if (receiver != null)
            //{
            //    receiver.Close();
            //    receiver = null;
            //}

            if (m_Thread != null)
                m_Thread.Interrupt();
            Application.Current.Shutdown();

        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_threeDWindow == null)
            {
                m_threeDWindow = new ThreeD();
                m_threeDWindow.grab();
            }
            else
            {
                m_threeDWindow.Show();
            }
        }

        Stopwatch FWatch = Stopwatch.StartNew();
        double FLastMillis;
        double FDiff;
        double FDiffTimeStamp;
        int counter;
        int FLastTimestamp;

        //private void setupMQListener()
        //{
        //    int port = 8000;
        //    if (receiver != null)
        //    {
        //        receiver.Dispose();
        //    }
        //    receiver = new OscReceiver(9000);
        //    receiver.Connect();

        //    if (m_Thread != null)
        //    {
        //        m_Thread.Abort();
        //        m_Thread = new Thread(new ThreadStart(ListenLoop));
        //        m_Thread.IsBackground = true;
        //        m_Thread.Start();
        //    }

        //    if (sender != null)
        //    {
        //        sender.Dispose();
        //    }

        //    sender = new OscSender(MQ_IPAddress, port);
        //    sender.Connect();

        //    sender.Send(new OscMessage("/feedback/pb+exec"));
        //}

        private void AdonisWindow_PreviewLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            var window = (Window)sender;
            window.Topmost = (bool)alwaysOnTop.IsChecked;
        }

        private void alwaysOnTop_Unchecked(object sender, RoutedEventArgs e)
        {
            AdonisWindow_PreviewLostKeyboardFocus(this, null);
        }

        private void alwaysOnTop_Checked(object sender, RoutedEventArgs e)
        {
            AdonisWindow_PreviewLostKeyboardFocus(this, null);
        }

        private void FollwSpot_dataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }

        private byte ArtNetSequence = 0;
        public void PointSpots()
        {
            //foreach (Follow_Spot spot in m_spots)
            //{
            //    Vector3D p = (spot.Target - spot.Location);
            //    Point3D direction = Spherical.ToSpherical(-p.Y, p.X, p.Z);
            //    direction.Y += 90;

            //    direction = Spherical.MinSphericalMove(new Point3D(1, spot.Tilt, spot.Pan), direction);
            //    spot.Tilt = direction.Y;
            //    spot.Pan = direction.Z;
            //}

            //updateDMX();
            Smoother(null, null);
        }

        System.Timers.Timer timer = null;
        readonly object timerLock = new object();

        void Smoother(object sender, ElapsedEventArgs e)
        {
            bool isMoving = false;
            lock (timerLock)
            {
                if (timer == null)
                {
                    timer = new System.Timers.Timer();
                    timer.Elapsed += Smoother;
                    timer.AutoReset = false;
                    timer.Interval = 25;
                }
                else
                {
                    timer.Stop();
                }

                foreach (Follow_Spot spot in m_spots)
                {
                    double minVelocity = 0.02;
                    Vector3D delta = spot.Target - spot.CurrentTarget;

                    spot.Acceleration = 0.1 * delta - (0.5 * spot.Velocity);

                    spot.Velocity += spot.Acceleration;


                    if (spot.Velocity.Length > minVelocity)
                        isMoving = true;

                    spot.CurrentTarget += spot.Velocity;
                    spot.Velocity *= 0.5;

                    Vector3D p = (spot.CurrentTarget - spot.Location);
                    Point3D direction = Spherical.ToSpherical(-p.Y, p.X, p.Z);
                    direction.Y += 90;

                    direction = Spherical.MinSphericalMove(new Point3D(1, spot.Tilt, spot.Pan), direction);
                    spot.Tilt = direction.Y;
                    spot.Pan = direction.Z;
                }

                updateDMX();

                if (isMoving)
                {
                    timer.Start();
                }
            }
        }
        public void updateDMX(byte[] packet)
        {
            ArtNetSequence++;

            m_TXsocket.Send(new ArtNetDmxPacket
            {
                Sequence = ArtNetSequence,
                Physical = 1,
                Universe = (short)ARTNET_RXUniverse,
                DmxData = packet
            });
            ArtNetactivity(2);
        }

        public void updateDMX()
        {
            //byte[] packet = new byte[512];

            //foreach (Follow_Spot spot in m_spots)
            //{
            //    int PanDMX = (int)Math.Round((((spot.Pan + 270.0) / 540.0) * 65535.0),0);
            //    int TiltDMX = (int)Math.Round((((spot.Tilt + 135.0) / 270.0) * 65535.0),0);

            //    packet[spot.Address - 1] = (byte)(PanDMX / 256);
            //    packet[spot.Address] = (byte)(PanDMX % 256);
            //    packet[spot.Address + 1] = (byte)(TiltDMX / 256);
            //    packet[spot.Address + 2] = (byte)(TiltDMX % 256);
            //}


            MemoryStream stream = new MemoryStream();
            BinaryFormatter serializer = new BinaryFormatter();
            serializer.Serialize(stream, m_spots);
            byte[] buffer = new byte[stream.Length+3];
            buffer[0] = 2; // Position Update
            buffer[1] = (byte)(stream.Length / 256);
            buffer[2] = (byte)(stream.Length & 0xFF);
            stream.ToArray();

            Array.Copy(stream.ToArray(), 0, buffer, 3, stream.Length);

            try
            {
                if (client.Connected)
                    client.Send(buffer, (int)stream.Length + 3, SocketFlags.None);
            }
            catch (Exception w)
            {
                client.Close();
            }

            //updateDMX(packet);
        }

        // The port number for the remote device.  
        private const int port = 11000;

        // ManualResetEvent instances signal completion.  
        private ManualResetEvent connectDone =
            new ManualResetEvent(false);
        private ManualResetEvent sendDone =
            new ManualResetEvent(false);
        private ManualResetEvent receiveDone =
            new ManualResetEvent(false);

        // The response from the remote device.  
        private String response = String.Empty;

        Thread clientThread;
        Socket client;
        IPEndPoint remoteEP;

        public void StartClient()
        {
            // Connect to a remote device.  
            try
            {
                if (clientThread == null)
                {
                    clientThread = new Thread(new ThreadStart(clientLoop));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        void clientLoop()
        {
            // Connect to the remote endpoint.  
            while (true)
            {
                try
                {
                    if (context != null)
                    {
                        context.Post(delegate (object dummy)
                        {
                            ConnectionLED.Fill = RedFill;
                        }, null);
                    }

                    // Create a TCP/IP socket.  
                    IPAddress ipAddress = IPAddress.Parse("10.0.0.50");
                    remoteEP = new IPEndPoint(ipAddress, port);

                    client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    client.Connect(remoteEP);

                    byte[] buffer = new byte[1024];

                    buffer[0] = 1; // ClientID
                    buffer[1] = 1;

                    client.Send(buffer, 2, SocketFlags.None);
                    activityMQ(2);

                    if (m_spots.Count>0)
                        m_spots[0].IsLeadSpot = true;

                    int count = 0;
                    do
                    {
                        count = client.Receive(buffer, 1, SocketFlags.None);
                        activityMQ(1);
                        switch (buffer[0])
                        {
                            case 0:
                                Console.WriteLine("Server Command: " + buffer[0]);
                                break;
                            default:
                                Console.WriteLine("Server Command: " + buffer[0]);
                                break;
                        }

                    } while (count>0);

                    throw new Exception("Read zero");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Socket Exception:" + e);
                    try
                    {
                        client.Close();
                    } catch (Exception e2)
                    {
                        Console.WriteLine("Socket Exception2:" + e2);
                    }

                    if (context != null)
                    {
                        context.Post(delegate (object dummy)
                        {
                            ConnectionLED.Fill = RedFill;
                        }, null);
                    }
                    Thread.Sleep(1000);
                }
            }
        }

    }

}
