using BaseLibS.Parse.Endian;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BitTorrent
{
    public enum TrackerEvent
    {
        Started,
        Paused,
        Stopped
    }

    public class Tracker
    {
        public event EventHandler<List<IPEndPoint>> PeerListUpdated;

        public string Address { get; private set; }

        public DateTime LastPeerRequest { get; private set; } = DateTime.MinValue;
        public TimeSpan PeerRequestInterval { get; private set; } = TimeSpan.FromMinutes(30);

        private HttpWebRequest httpWebRequest;

        public Tracker(string address)
        {
            Address = address;
        }

        public void Update(Torrent torrent, TrackerEvent ev, string id, int port)
        {
            if (ev == TrackerEvent.Started && DateTime.UtcNow < LastPeerRequest.Add(PeerRequestInterval))
                return;

            this.LastPeerRequest = DateTime.UtcNow;

            string url = String.Format("{0}?info_hash={1}&peer_id={2}&port={3}&uploaded={4}&downloaded={5}&left={6}&event={7}&compact=1",
                     Address, torrent.UrlSafeStringInfohash,
                     id, port,
                     torrent.Uploaded, torrent.Downloaded, torrent.Left,
                     Enum.GetName(typeof(TrackerEvent), ev).ToLower());

            Request(url);
        }

        public void ResetLastRequest()
        {
            this.LastPeerRequest = DateTime.MinValue;
        }

        private void Request(string url)
        {
            this.httpWebRequest = (HttpWebRequest)HttpWebRequest.Create(url);
            this.httpWebRequest.BeginGetResponse(HandleResponse, null);
        }

        private void HandleResponse(IAsyncResult result)
        {
            byte[] data;

            using (HttpWebResponse response = (HttpWebResponse)httpWebRequest.EndGetResponse(result))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("error reaching tracker " + this + ": " + response.StatusCode + " " + response.StatusDescription);
                    return;
                }

                using (Stream stream = response.GetResponseStream())
                {
                    data = new byte[response.ContentLength];
                    stream.Read(data, 0, Convert.ToInt32(response.ContentLength));
                }
            }

            Dictionary<string, object> info = BEncoding.Decode(data) as Dictionary<string, object>;

            if (info == null)
            {
                Console.WriteLine("unable to decode tracker announce response");
                return;
            }

            this.PeerRequestInterval = TimeSpan.FromSeconds((long)info["interval"]);
            byte[] peerInfo = (byte[])info["peers"];

            List<IPEndPoint> peers = new List<IPEndPoint>();
            for (var i = 0; i < peerInfo.Length / 6; i++)
            {
                int offset = i * 6;
                string address = peerInfo[offset] + "." + peerInfo[offset + 1] + "." + peerInfo[offset + 2] + "." + peerInfo[offset + 3];
                // don't sure
                int port = EndianBitConverter.Big.ToChar(peerInfo, offset + 4);

                peers.Add(new IPEndPoint(IPAddress.Parse(address), port));
            }

            var handler = this.PeerListUpdated;
            if (handler != null)
                handler(this, peers);
        }
    }
}
