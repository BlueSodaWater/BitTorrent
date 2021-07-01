using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitTorrent
{
    public class Client
    {
        public int Port { get; private set; }
        public Torrent Torrent { get; private set; }

        private static int maxLeechers = 5;
        private static int maxSeeders = 5;

        private static int maxUploadBytesPerSecond = 16384;
        private static int maxDownloadBytesPerSecond = 16384;

        private static TimeSpan peerTimeout = TimeSpan.FromSeconds(30);

        public string Id { get; private set; }

        private TcpListener listener;

        public ConcurrentDictionary<string, Peer> Peers { get; } = new ConcurrentDictionary<string, Peer>();
        public ConcurrentDictionary<string, Peer> Seeders { get; } = new ConcurrentDictionary<string, Peer>();
        public ConcurrentDictionary<string, Peer> Leechers { get; } = new ConcurrentDictionary<string, Peer>();

        private bool isStopping;
        private int isProcessPeers = 0;
        private int isProcessUploads = 0;
        private int isProcessDownloads = 0;

        private Random random = new Random();

        public Client(int port, string torrentPath, string downloadPath)
        {
            Id = "";
            for (int i = 0; i < 20; i++)
                Id += (random.Next(0, 10));

            Port = port;

            Torrent = Torrent.LoadFromFile(torrentPath, downloadPath);
            Torrent.PieceVerified += HandlePieceVerified;
            Torrent.PeerListUpdated += HandlePeerListUpdated;

            Log.WriteLine(Torrent);
        }

        public void Start()
        {
            Log.WriteLine("starting client");

            isStopping = false;

            Torrent.ResetTrackersLastRequest();

            EnablePeerConnections();

            // tracker thread
            new Thread(new ThreadStart(() =>
            {
                while (!isStopping)
                {
                    Torrent.UpdatedTrackers(TrackerEvent.Started, Id, Port);
                    Thread.Sleep(10000);
                }
            })).Start();

            // peer thread
            new Thread(new ThreadStart(() =>
            {
                while (!isStopping)
                {
                    ProcessPeers();
                    Thread.Sleep(1000);
                }
            })).Start();

            // upload thread
            new Thread(new ThreadStart(() =>
            {
                while (!isStopping)
                {
                    ProcessUploads();
                    Thread.Sleep(1000);
                }
            })).Start();

            // download thread
            new Thread(new ThreadStart(() =>
            {
                while (!isStopping)
                {
                    ProcessDownloads();
                    Thread.Sleep(1000);
                }
            })).Start();
        }

        public void Stop()
        {
            Log.WriteLine("stopping client");

            isStopping = true;
            DisablePeerConnections();
            Torrent.UpdatedTrackers(TrackerEvent.Stopped, Id, Port);
        }

        private static IPAddress LocalIPAddress
        {
            get
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip;
                }
                throw new Exception("Local IP Address Not Found!");
            }
        }

        private void HandlePeerListUpdated(object sender, List<IPEndPoint> endPoints)
        {
            IPAddress local = LocalIPAddress;

            foreach (var endPoint in endPoints)
            {
                if (endPoint.Address.Equals(local) && endPoint.Port == Port)
                    continue;

                AddPeer(new Peer(Torrent, Id, endPoint));
            }

            Log.WriteLine("received peer information from " + (Tracker)sender);
            Log.WriteLine("peer count: " + Peers.Count);
        }

        private void EnablePeerConnections()
        {
            listener = new TcpListener(new IPEndPoint(IPAddress.Any, Port));
            listener.Start();
            listener.BeginAcceptTcpClient(new AsyncCallback(HandleNewConnection), null);

            //Log.WriteLine("started listening for incoming peer connections on port " + Port);
        }

        private void HandleNewConnection(IAsyncResult ar)
        {
            if (listener == null)
                return;

            TcpClient client = listener.EndAcceptTcpClient(ar);
            listener.BeginAcceptTcpClient(new AsyncCallback(HandleNewConnection), null);
            AddPeer(new Peer(Torrent, Id, client));
        }

        private void DisablePeerConnections()
        {
            listener.Stop();
            listener = null;

            foreach (var peer in Peers)
                peer.Value.Disconnect();

            Log.WriteLine("stopped listening for incoming peer connections on port " + Port);
        }

        private void AddPeer(Peer peer)
        {
            peer.BlockRequested += HandleBlockRequested;
        }

        private void HandlePeerDisconnected(object sender, EventArgs args)
        {

        }

        private void HandlePeerStateChanged(object sender, EventArgs args)
        {
            ProcessPeers();
        }

        private void HandlePieceVerified(object sender, int index)
        {
            ProcessPeers();

            foreach (var peer in Peers)
            {
                if (!peer.Value.IsHandshakeReceived || !peer.Value.IsHandshakeSent)
                    continue;

                peer.Value.SendHave(index);
            }
        }

        private void ProcessPeers()
        {
            if (Interlocked.Exchange(ref isProcessPeers, 1) == 1)
                return;

            foreach (var peer in Peers.OrderByDescending(x => x.Value.PiecesRequiredAvailable))
            {
                if (DateTime.UtcNow > peer.Value.LastActive.Add(peerTimeout))
                {
                    peer.Value.Disconnect();
                    continue;
                }

                if (!peer.Value.IsHandshakeSent || !peer.Value.IsHandshakeReceived)
                    continue;

                if (Torrent.IsCompleted)
                    peer.Value.SendNotInterested();
                else
                    peer.Value.SendInterested();

                if (peer.Value.IsCompleted && Torrent.IsCompleted)
                {
                    peer.Value.Disconnect();
                    continue;
                }

                peer.Value.SendKeepAlive();

                // let them leech
                if (Torrent.IsStarted && Leechers.Count < maxLeechers)
                {
                    if (peer.Value.IsInterestedReceived && peer.Value.IsChokeSent)
                        peer.Value.SendUnchoke();
                }

                // ask to leech
                if (!Torrent.IsCompleted && Seeders.Count <= maxSeeders)
                {
                    if (!peer.Value.IsChokeReceived)
                        Seeders.TryAdd(peer.Key, peer.Value);
                }
            }

            Interlocked.Exchange(ref isProcessPeers, 0);
        }
    }
}
