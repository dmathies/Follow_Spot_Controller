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
using System.IO;

namespace MidiApp
{

    public partial class MainWindow : AdonisUI.Controls.AdonisWindow
    {
        public static int clientID = -1;

        public Thread m_Thread = null;
        public Thread activity_Thread = null;
        public Thread ArtNetactivity_Thread = null;
        public ArtNetSocket m_socket = null;
        public ArtNetSocket m_TXsocket = null;
        public static List<Follow_Spot> m_spots = new List<Follow_Spot>();
        public static List<Marker> m_markers = new List<Marker>();
        public static string serverIP_Addres;

        public Thread mqConnection_Thread = null;

        public string resourceFileName = @"Markers.json";
        public static dynamic AppResources;
        public Thread m_ResourceLoader_Thread = null;

        public ThreeD m_threeDWindow = null;

        SynchronizationContext context;

        public MainWindow()
        {
            InitializeComponent();
            context = SynchronizationContext.Current;

            

        }

        public void updateResources()
        {
            ARTNET_RXIPAddress = IPAddress.Parse((string)AppResources.Network.ArtNet.RXIP);
            ARTNET_RXSubNetMask = IPAddress.Parse((string)AppResources.Network.ArtNet.RXSubNetMask);
            ARTNET_RXUniverse = (int)AppResources.Network.ArtNet.Universe;


            m_spots.Clear();

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

            if (AppResources.Markers !=null)
            foreach (dynamic v in AppResources.Markers)
            {
                Marker m = new Marker();
                m.clientID = MainWindow.clientID;
                m.markerID = m_markers.Count;
                m.position = new Point3D(1, 2, 3);
            }


            context.Post(delegate (object dummy)
            {
                if (m_threeDWindow != null)
                {
                    m_threeDWindow.Close();
                }

                m_threeDWindow = new ThreeD();
                m_threeDWindow.setActive(false);
                m_threeDWindow.grab();
                FollwSpot_dataGrid.ItemsSource = m_spots;
                m_threeDWindow?.UpdateModel();
            }, null);

            if (m_socket != null)
                m_socket.Close();

            //ArtNetListner();
        }

        IPAddress ML_IPAddress = IPAddress.Parse(MainWindow.serverIP_Addres);
        IPAddress ARTNET_RXIPAddress = null;
        IPAddress ARTNET_RXSubNetMask = null;
        int ARTNET_RXUniverse = 0;

        public void SaveMarkers()
        {
            try
            {
                string res = System.IO.File.ReadAllText(resourceFileName);
                System.IO.File.WriteAllText(resourceFileName + ".bak", res);
            } catch (FileNotFoundException)
            {

            }

            System.IO.File.WriteAllText(resourceFileName, Newtonsoft.Json.JsonConvert.SerializeObject(m_markers));
        }

        public void LoadMarkers()
        {
            try
            {
                var res = System.IO.File.ReadAllText(resourceFileName);
                dynamic markers = Newtonsoft.Json.JsonConvert.DeserializeObject(res);

                if (markers!= null)
                {
                    m_markers.Clear();
                    foreach (dynamic v in markers)
                    {
                        Marker m = new Marker();
                        m.clientID = v.clientID;
                        m.markerID = v.markerID;
                        string[] coords = ((string)v.position).Split(',');
                        m.position = new Point3D(Double.Parse(coords[0]), Double.Parse(coords[1]), Double.Parse(coords[2]));
                        m_markers.Add(m);
                    }
                }

            }
            catch (System.IO.FileNotFoundException)
            {
                MessageBox.Show("Cannot find resource file\n" + resourceFileName, "File Not Found", MessageBoxButton.OK, MessageBoxImage.Stop);
                Close();
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
            m_socket.NewPacket += ArtNet_NewPacket;
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
            try
            {
                // Convert m_spots to SpotState list and wrap in envelope
                var states = new System.Collections.Generic.List<MidiApp.Common.SpotState>();
                foreach (var spot in m_spots)
                {
                    states.Add(new MidiApp.Common.SpotState
                    {
                        Pan = spot.Pan,
                        Tilt = spot.Tilt,
                        Zoom = 0,  // Controller's Follow_Spot doesn't track zoom
                        HeightOffset = 0.0,  // Controller's Follow_Spot doesn't track height offset
                        TargetX = spot.Target.X,
                        TargetY = spot.Target.Y,
                        TargetZ = spot.Target.Z,
                        MouseControlID = spot.MouseControlID
                    });
                }

                // Wrap in MessageEnvelope for versioning
                var env = new MidiApp.Common.MessageEnvelope 
                { 
                    Version = MidiApp.Common.Protocol.ProtocolVersion, 
                    Type = MidiApp.Common.MessageType.SpotUpdate, 
                    Payload = states 
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(env);
                byte[] payload = Encoding.UTF8.GetBytes(json);
                int len = payload.Length;

                byte[] buffer = new byte[len + 3];
                buffer[0] = (byte)MidiApp.Common.MessageType.SpotUpdate;
                buffer[1] = (byte)(len / 256);
                buffer[2] = (byte)(len & 0xFF);
                Array.Copy(payload, 0, buffer, 3, len);

                if (client.Connected)
                    client.Send(buffer, len + 3, SocketFlags.None);
            }
            catch (Exception w)
            {
                Console.WriteLine("updateDMX error: " + w);
                try { client.Close(); } catch { }
            }
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

        private byte[] SerializeMessageEnvelope(MidiApp.Common.ClientMessage msg)
        {
            var env = new MidiApp.Common.MessageEnvelope { Version = MidiApp.Common.Protocol.ProtocolVersion, Type = MidiApp.Common.MessageType.Message, Payload = msg };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(env);
            return Encoding.UTF8.GetBytes(json);
        }

        public void SendClientMessage(int clientID, String message, String spots, int timeout)
        {
            string[] spotParts = spots.Split(',');
            MidiApp.Common.ClientMessage msg = new MidiApp.Common.ClientMessage
            {
                clientID = clientID,
                message = WebUtility.UrlDecode(message),
                spots = new int[spotParts.Length]
            };
            msg.zooms = new int[msg.spots.Length];
            msg.HeightOffsets = new double[msg.spots.Length];
            msg.timeout = timeout;

            for (int i = 0; i < spotParts.Length; i++)
            {
                msg.spots[i] = Int32.Parse(spotParts[i]);
                msg.zooms[i] = 255;
                msg.HeightOffsets[i] = 0.0;
            }

            try
            {
                byte[] payload = SerializeMessageEnvelope(msg);
                byte[] buffer = new byte[payload.Length + 3];
                buffer[0] = (byte)MessageType.Message;
                buffer[1] = (byte)(payload.Length / 256);
                buffer[2] = (byte)(payload.Length & 0xFF);
                Array.Copy(payload, 0, buffer, 3, payload.Length);

                if (client != null && client.Connected)
                {
                    client.Send(buffer, buffer.Length, SocketFlags.None);
                    activityMQ(2);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to send message envelope: " + e.Message);
            }
        }

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
                    IPAddress ipAddress = ML_IPAddress;
                    remoteEP = new IPEndPoint(ipAddress, port);

                    client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    client.Connect(remoteEP);

                    byte[] buffer = new byte[1024];

                    buffer[0] = 1; // Client Connect
                    buffer[1] = (byte) MainWindow.clientID; 

                    client.Send(buffer, 2, SocketFlags.None);
                    activityMQ(2);

                    if (m_spots.Count>0)
                        m_spots[0].IsLeadSpot = true;

                    int count = 0;
                    do
                    {
                        count = client.Receive(buffer, 1, SocketFlags.None);
                        activityMQ(1);
                        switch ((MessageType)buffer[0])
                        {
                            case MessageType.Initialize:
                                Console.WriteLine("Server Command: " + buffer[0]);
                                break;
                            case MessageType.ConfigureClient:
                                {
                                    client.Receive(buffer, 1, 3, SocketFlags.None);
                                    int length = buffer[1] * 256 + buffer[2];
                                    clientID = (int)buffer[3]+1;
                                    Console.WriteLine($"Server Update: {buffer[0]}, client ID: {clientID}");

                                    byte[] rcv_buffer = new byte[length];
                                    int recieved = 0;
                                    while (recieved < length)
                                    {
                                        recieved += client.Receive(rcv_buffer, recieved, length - recieved, SocketFlags.None);
                                    }

                                    try
                                    {
                                        string json = Encoding.UTF8.GetString(rcv_buffer);
                                        // defensive: trim any leading non-JSON bytes (sometimes TCP stream framing can slip)
                                        int start = json.IndexOfAny(new char[] { '{', '[' });
                                        if (start > 0)
                                        {
                                            // main app LogDebug not available in controller - write to console
                                            Console.WriteLine($"Trimmed {start} leading bytes from SpotUpdate payload");
                                            json = json.Substring(start);
                                        }
                                        var env = Newtonsoft.Json.JsonConvert.DeserializeObject<MidiApp.Common.MessageEnvelope>(json);
                                        if (env == null) throw new Exception("Envelope deserialized null");
                                        if (env.Version != MidiApp.Common.Protocol.ProtocolVersion) throw new Exception($"Unsupported protocol version: {env.Version}");
                                        if (env.Type != MidiApp.Common.MessageType.ConfigureClient) throw new Exception($"Unexpected envelope type: {env.Type}");

                                        // Payload is dynamic JSON representing the resources; convert to object then update
                                        var payloadObj = env.Payload as Newtonsoft.Json.Linq.JObject ?? Newtonsoft.Json.Linq.JObject.FromObject(env.Payload);
                                        AppResources = payloadObj;
                                        updateResources();

                                        if (context != null)
                                        {
                                            context.Post(delegate (object dummy)
                                            {
                                                ControllerID.Content = ""+clientID;
                                            }, null);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Failed to parse ConfigureClient envelope: " + ex.Message);
                                    }
                                }
                                break;
                            case MessageType.SpotUpdate: // Spot position update
                                {
                                    client.Receive(buffer, 1, 2, SocketFlags.None);
                                    int res_length = buffer[1] * 256 + buffer[2];
                                    byte[] rcv_buffer = new byte[res_length];
                                    int recieved = 0;
                                    while (recieved < res_length)
                                    {
                                        recieved += client.Receive(rcv_buffer, recieved, res_length - recieved, SocketFlags.None);
                                    }

                                    // Deserialize JSON spot update envelope (UTF8)
                                    try
                                    {
                                        string json = Encoding.UTF8.GetString(rcv_buffer);
                                        int start = json.IndexOfAny(new char[] { '{', '[' });
                                        if (start > 0)
                                        {
                                            Console.WriteLine($"Trimmed {start} leading bytes from Message payload");
                                            json = json.Substring(start);
                                        }
                                        var env = Newtonsoft.Json.JsonConvert.DeserializeObject<MidiApp.Common.MessageEnvelope>(json);
                                        if (env == null) throw new Exception("Envelope deserialized null");
                                        if (env.Version != MidiApp.Common.Protocol.ProtocolVersion) throw new Exception($"Unsupported protocol version: {env.Version}");
                                        if (env.Type != MidiApp.Common.MessageType.SpotUpdate) throw new Exception($"Unexpected envelope type: {env.Type}");

                                        var arr = ((Newtonsoft.Json.Linq.JArray)env.Payload).ToObject<System.Collections.Generic.List<MidiApp.Common.SpotState>>();
                                        for (int i = 0; i < MainWindow.m_spots.Count && i < arr.Count; i++)
                                        {
                                            if (MainWindow.m_spots[i].MouseControlID != clientID)
                                            {
                                                var s = arr[i];
                                                MainWindow.m_spots[i].Pan = s.Pan;
                                                MainWindow.m_spots[i].Tilt = s.Tilt;
                                                MainWindow.m_spots[i].Target = new System.Windows.Media.Media3D.Point3D(s.TargetX, s.TargetY, s.TargetZ);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Failed to parse SpotUpdate envelope: " + ex.Message);
                                    }
                                }
                                break;

                            case MessageType.Message: //Message box
                                {
                                    client.Receive(buffer, 1, 2, SocketFlags.None);
                                    int res_length = buffer[1] * 256 + buffer[2];
                                    byte[] rcv_buffer = new byte[res_length];
                                    int recieved = 0;
                                    while (recieved < res_length)
                                    {
                                        recieved += client.Receive(rcv_buffer, recieved, res_length - recieved, SocketFlags.None);
                                    }

                                    // Message payload is a JSON envelope (ClientMessage equivalent)
                                    try
                                    {
                                        string json = Encoding.UTF8.GetString(rcv_buffer);
                                        var env = Newtonsoft.Json.JsonConvert.DeserializeObject<MidiApp.Common.MessageEnvelope>(json);
                                        if (env == null) throw new Exception("Envelope deserialized null");
                                        if (env.Version != MidiApp.Common.Protocol.ProtocolVersion) throw new Exception($"Unsupported protocol version: {env.Version}");
                                        if (env.Type != MidiApp.Common.MessageType.Message) throw new Exception($"Unexpected envelope type: {env.Type}");

                                        var cm = ((Newtonsoft.Json.Linq.JObject)env.Payload).ToObject<MidiApp.Common.ClientMessage>();
                                        if (context != null)
                                        {
                                            context.Post(delegate (object dummy)
                                            {
                                                if (cm.spots?.Length >= 0)
                                                {
                                                    foreach (Follow_Spot spot in m_spots)
                                                    {
                                                        spot.IsLeadSpot = spot.Head == cm.spots[0];
                                                        spot.MouseControlID = (spot.Head == cm.spots[0]) ? cm.clientID : -1;
                                                    }
                                                }
                                                else
                                                {
                                                    foreach (Follow_Spot spot in m_spots)
                                                    {
                                                        spot.IsLeadSpot = false;
                                                        spot.MouseControlID = -1;
                                                    }
                                                }

                                            }, null);
                                        }

                                        if (m_threeDWindow != null)
                                        {
                                            if (context != null)
                                            {
                                                context.Post(delegate (object dummy)
                                                {
                                                    // Refresh the 3D view when lead spot changes
                                                    m_threeDWindow.UpdateModel();

                                                    if (!string.IsNullOrEmpty(cm.message))
                                                    {
                                                        m_threeDWindow.MessagePopup.Content = cm.message;
                                                        m_threeDWindow.MessagePopup.Visibility = Visibility.Visible;
                                                    }
                                                    else
                                                    {
                                                        m_threeDWindow.MessagePopup.Visibility = Visibility.Hidden;
                                                    }
                                                }, null);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Failed to parse Message envelope: " + ex.Message);
                                    }
                                }
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
                            ControllerID.Content = "?";
                        }, null);
                    }
                    Thread.Sleep(1000);
                }
            }
        }

    }

}

[Serializable]
public struct clientMesage
{
    public int clientID;
    public String message;
    public int[] spots;
    public int timeout;
}
enum MessageType
{
    Initialize =0,
    ConfigureClient =1,
    SpotUpdate =2,         // Spot position update
    Message = 3            //Message box
}


public class Marker
{
    public Point3D position;
    public int clientID;
    public int markerID;
}