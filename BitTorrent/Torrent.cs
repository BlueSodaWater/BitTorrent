using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BitTorrent
{
    public class FileItem
    {
        public string Path;
        public long Size;
        public long Offset;

        public string FormattedSize;
    }

    public class Torrent
    {
        #region Events

        public event EventHandler<int> PieceVerified;
        public event EventHandler<List<IPEndPoint>> PeerListUpdated;

        #endregion

        #region Properties

        public string Name { get; private set; }
        public bool? IsPrivate { get; private set; }
        public List<FileItem> Files { get; private set; } = new List<FileItem>();
        public string FileDirectory { get { return (Files.Count > 1 ? Name + Path.DirectorySeparatorChar : ""); } }
        public string DownloadDirectory { get; private set; }

        public List<Tracker> Trackers { get; } = new List<Tracker>();
        public string Comment { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public Encoding Encoding { get; set; }

        public int BlockSize { get; private set; }
        public int PieceSize { get; private set; }
        public long TotalSize { get { return Files.Sum(x => x.Size); } }

        public string FormattedPieceSize { get { return ByteToString(PieceSize); } }
        public string FormattedTotalSize { get { return ByteToString(TotalSize); } }

        public int PieceCount { get { return PieceHashes.Length; } }

        public byte[][] PieceHashes { get; private set; }
        public bool[] IsPieceVerified { get; private set; }
        public bool[][] IsBlockAcquired { get; private set; }

        public string VerifiedPiecesString { get { return String.Join("", IsPieceVerified.Select(x => x ? 1 : 0)); } }
        public int VerifiedPieceCount { get { return IsPieceVerified.Count(x => x); } }
        public double VerifiedRatio { get { return VerifiedPieceCount / (double)PieceCount; } }
        public bool IsCompleted { get { return VerifiedPieceCount == PieceCount; } }
        public bool IsStarted { get { return VerifiedPieceCount > 0; } }

        public long Uploaded { get; set; } = 0;
        public long Downloaded { get { return PieceSize * VerifiedPieceCount; } }
        public long Left { get { return TotalSize - Downloaded; } }

        public byte[] Infohash { get; private set; } = new byte[20];
        public string HexStringInfohash { get { return string.Join("", this.Infohash.Select(x => x.ToString("x2"))); } }
        public string UrlSafeStringInfohash { get { return Encoding.UTF8.GetString(WebUtility.UrlEncodeToBytes(this.Infohash, 0, 20)); } }

        #endregion

        #region Fields

        private object[] fileWriteLocks;
        private static SHA1 sha1 = SHA1.Create();

        #endregion

        public Torrent(string name, string location, List<FileItem> files, List<string> trackers, int pieceSize, byte[] pieceHashes = null, int blockSize = 16384, bool? isPrivate = false)
        {
            this.Name = name;
            this.DownloadDirectory = location;
            this.Files = files;
            this.fileWriteLocks = new object[files.Count];
            for (var i = 0; i < this.Files.Count; i++)
                fileWriteLocks[i] = new object();

            if (trackers != null)
            {
                foreach (string url in trackers)
                {
                    Tracker tracker = new Tracker(url);
                    this.Trackers.Add(tracker);
                    tracker.PeerListUpdated += HandlePeerListUpdated;
                }
            }

            this.PieceSize = pieceSize;
            this.BlockSize = blockSize;
            this.IsPrivate = isPrivate;

            int count = Convert.ToInt32(Math.Ceiling(TotalSize / Convert.ToDouble(pieceSize)));

            this.PieceHashes = new byte[count][];
            this.IsPieceVerified = new bool[count];
            this.IsBlockAcquired = new bool[count][];

            for (var i = 0; i < PieceCount; i++)
                IsBlockAcquired[i] = new bool[GetBlockCount(i)];

            if (pieceHashes == null)
            {
                for (int i = 0; i < PieceCount; i++)
                    this.PieceHashes[i] = GetHash(i);
            }
            else
            {
                for (int i = 0; i < PieceCount; i++)
                {
                    this.PieceHashes[i] = new byte[20];
                    Buffer.BlockCopy(pieceHashes, i * 20, this.PieceHashes[i], 0, 20);
                }
            }

            object info = TorrentInfoToBEncodingObject(this);
            byte[] bytes = BEncoding.Encode(info);
            Infohash = SHA1.Create().ComputeHash(bytes);

            for (var i = 0; i < PieceCount; i++)
                this.Verify(i);
        }

        private void Verify(int piece)
        {
            byte[] hash = GetHash(piece);

            bool isVerified = (hash != null && hash.SequenceEqual(PieceHashes[piece]));

            if (isVerified)
            {
                IsPieceVerified[piece] = true;

                for (var j = 0; j < IsBlockAcquired[piece].Length; j++)
                    IsBlockAcquired[piece][j] = true;

                var handler = PieceVerified;
                if (handler != null)
                    handler(this, piece);

                return;
            }

            IsPieceVerified[piece] = false;

            if (IsBlockAcquired[piece].All(x => x))
            {
                for (var j = 0; j < IsBlockAcquired[piece].Length; j++)
                    IsBlockAcquired[piece][j] = false;
            }
        }

        private object TorrentInfoToBEncodingObject(Torrent torrent)
        {
            throw new NotImplementedException();
        }

        private byte[] GetHash(int piece)
        {
            byte[] data = ReadPiece(piece);

            if (data == null)
                return null;

            return sha1.ComputeHash(data);
        }

        public byte[] Read(long start, int length)
        {
            long end = start + length;
            byte[] buffer = new byte[length];

            for (var i = 0; i < Files.Count; i++)
            {
                if ((start < Files[i].Offset && end < Files[i].Offset) ||
                    start > Files[i].Offset + Files[i].Size && end > Files[i].Offset + Files[i].Size)
                    continue;

                string filePath = DownloadDirectory + Path.DirectorySeparatorChar + FileDirectory + Files[i].Path;

                if (!File.Exists(filePath))
                    return null;

                long fstart = Math.Max(0, start - Files[i].Offset);
                long fend = Math.Min(end - Files[i].Offset, Files[i].Size);
                int flength = Convert.ToInt32(fend - fstart);
                int bstart = Math.Max(0, Convert.ToInt32(Files[i].Offset - start));

                using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(fstart, SeekOrigin.Begin);
                    stream.Read(buffer, bstart, flength);
                }
            }

            return buffer;
        }

        public void Write(long start, byte[] bytes)
        {
            long end = start + bytes.Length;

            for (var i = 0; i < Files.Count; i++)
            {
                if ((start < Files[i].Offset && end < Files[i].Offset) ||
                        (start > Files[i].Offset + Files[i].Size && end > Files[i].Offset + Files[i].Size))
                    continue;

                string filePath = DownloadDirectory + Path.DirectorySeparatorChar + FileDirectory + Files[i].Path;

                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                lock (fileWriteLocks[i])
                {
                    using (Stream stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        long fstart = Math.Max(0, start - Files[i].Offset);
                        long fend = Math.Min(end - Files[i].Offset, Files[i].Size);
                        int flength = Convert.ToInt32(fend - fstart);
                        int bstart = Math.Max(0, Convert.ToInt32(Files[i].Offset - start));

                        stream.Seek(fstart, SeekOrigin.Begin);
                        stream.Write(bytes, bstart, flength);
                    }
                }
            }
        }

        public byte[] ReadPiece(int piece)
        {
            return Read(piece * PieceSize, GetPieceSize(piece));
        }

        public byte[] ReadBlock(int piece, int offset, int length)
        {
            return Read(piece * PieceSize + offset, length);
        }

        public void WriteBlock(int piece, int block, byte[] bytes)
        {
            Write(piece * PieceSize + block * BlockSize, bytes);
            IsBlockAcquired[piece][block] = true;
            Verify(piece);
        }

        public int GetPieceSize(int piece)
        {
            if (piece == PieceCount - 1)
            {
                int remainder = Convert.ToInt32(TotalSize % PieceSize);
                if (remainder != 0)
                    return remainder;
            }

            return PieceSize;
        }

        public int GetBlockSize(int piece, int block)
        {
            if (block == GetBlockCount(piece - 1))
            {
                int remainder = Convert.ToInt32(TotalSize % BlockSize);
                if (remainder != 0)
                    return remainder;
            }

            return BlockSize;
        }

        public int GetBlockCount(int piece)
        {
            return Convert.ToInt32(Math.Ceiling(GetPieceSize(piece) / (double)BlockSize));
        }

        public static string ByteToString(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (bytes == 0)
                return "0" + units[0];
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return num + units[place];
        }

        public static Torrent LoadFromFile(string filePath, string downloadPath)
        {
            object obj = BEncoding.DecodeFile(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);

            return BEncodingObjectToTorrent(obj, name, downloadPath);
        }

        public static void SaveToFile(Torrent torrent)
        {
            object obj = TorrentToBEcodingObject(torrent);

            BEncoding.EncodeToFile(obj, torrent.Name + ".torrent");
        }

        public static long DateTimeToUnixTimestamp(DateTime time)
        {
            return Convert.ToInt64((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
        }

        private static object TorrentToBEcodingObject(Torrent torrent)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            if (torrent.Trackers.Count == 1)
                dict["announce"] = Encoding.UTF8.GetBytes(torrent.Trackers[0].Address);
            else
                dict["announce"] = torrent.Trackers.Select(x => (object)Encoding.UTF8.GetBytes(x.Address)).ToList();
            dict["comment"] = Encoding.UTF8.GetBytes(torrent.Comment);
            dict["created by"] = Encoding.UTF8.GetBytes(torrent.CreatedBy);
            dict["creation date"] = DateTimeToUnixTimestamp(torrent.CreationDate);
            dict["encoding"] = Encoding.UTF8.GetBytes(Encoding.UTF8.WebName.ToUpper());
            dict["info"] = TorrentInfoToBEcodingObject(torrent);

            return dict;
        }

        private static object TorrentInfoToBEcodingObject(Torrent torrent)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            dict["piece length"] = (long)torrent.PieceSize;
            byte[] pieces = new byte[20 * torrent.PieceCount];
            for (int i = 0; i < torrent.PieceCount; i++)
                Buffer.BlockCopy(torrent.PieceHashes[i], 0, pieces, i * 20, 20);
            dict["pieces"] = pieces;

            if (torrent.IsPrivate.HasValue)
                dict["private"] = torrent.IsPrivate.Value ? 1L : 0L;

            if (torrent.Files.Count == 1)
            {
                dict["name"] = Encoding.UTF8.GetBytes(torrent.Files[0].Path);
                dict["length"] = torrent.Files[0].Size;
            }
            else
            {
                List<object> files = new List<object>();

                foreach (var f in torrent.Files)
                {
                    Dictionary<string, object> fileDict = new Dictionary<string, object>();
                    fileDict["path"] = f.Path.Split(Path.DirectorySeparatorChar).Select(x => (object)Encoding.UTF8.GetBytes(x)).ToList();
                    fileDict["length"] = f.Size;
                    files.Add(fileDict);
                }

                dict["files"] = files;
                dict["name"] = Encoding.UTF8.GetBytes(torrent.FileDirectory.Substring(0, torrent.FileDirectory.Length - 1));
            }

            return dict;
        }

        public static string DecodeUTF8String(object obj)
        {
            byte[] bytes = obj as byte[];

            if (bytes == null)
                throw new Exception("unable to decode utf-8 string, object is not a byte array");

            return Encoding.UTF8.GetString(bytes);
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        private static Torrent BEncodingObjectToTorrent(object bencoding, string name, string downloadPath)
        {
            Dictionary<string, object> obj = (Dictionary<string, object>)bencoding;

            if (obj == null)
                throw new Exception("not a torrent file");

            List<string> trackers = new List<string>();
            if (obj.ContainsKey("annouce"))
                trackers.Add(DecodeUTF8String(obj["annouce"]));

            if (!obj.ContainsKey("info"))
                throw new Exception("Missing info section");

            Dictionary<string, object> info = (Dictionary<string, object>)obj["info"];

            if (info == null)
                throw new Exception("error");

            List<FileItem> files = new List<FileItem>();

            if (info.ContainsKey("name") && info.ContainsKey("length"))
            {
                files.Add(new FileItem()
                {
                    Path = DecodeUTF8String(info["name"]),
                    Size = (long)info["length"]
                });
            }
            else if (info.ContainsKey("files"))
            {
                long running = 0;

                foreach (object item in (List<object>)info["files"])
                {
                    var dict = item as Dictionary<string, object>;

                    if (dict == null || !dict.ContainsKey("path") || !dict.ContainsKey("length"))
                        throw new Exception("error: incorrect file specification");

                    string path = String.Join(Path.DirectorySeparatorChar.ToString(), ((List<object>)dict["path"]).Select(x => DecodeUTF8String(x)));

                    long size = (long)dict["length"];

                    files.Add(new FileItem()
                    {
                        Path = path,
                        Size = size,
                        Offset = running
                    });

                    running += size;
                }
            }
            else
            {
                throw new Exception("error: no files specified in torrent");
            }

            if (!info.ContainsKey("piece length"))
                throw new Exception("error");
            int pieceSize = Convert.ToInt32(info["piece length"]);

            if (!info.ContainsKey("pieces"))
                throw new Exception("error");
            byte[] pieceHashes = (byte[])info["pieces"];

            bool? isPrivate = null;
            if (info.ContainsKey("private"))
                isPrivate = ((long)info["private"]) == 1L;

            Torrent torrent = new Torrent(name, downloadPath, files, trackers, pieceSize, pieceHashes, 16384, isPrivate);

            if (obj.ContainsKey("comment"))
                torrent.Comment = DecodeUTF8String(obj["comment"]);

            if (obj.ContainsKey("create by"))
                torrent.CreatedBy = DecodeUTF8String(obj["created by"]);

            if (obj.ContainsKey("creation date"))
                torrent.CreationDate = UnixTimeStampToDateTime(Convert.ToDouble(obj["creation date"]));

            if (obj.ContainsKey("encoding"))
                torrent.Encoding = Encoding.GetEncoding(DecodeUTF8String(obj["encoding"]));

            return torrent;
        }

        public static Torrent Create(string path, List<string> trackers = null, int pieceSize = 32768, string comment = "")
        {
            string name = "";
            List<FileItem> files = new List<FileItem>();

            if (File.Exists(path))
            {
                name = Path.GetFileName(path);

                long size = new FileInfo(path).Length;
                files.Add(new FileItem()
                {
                    Path = Path.GetFileName(path),
                    Size = size
                });
            }
            else
            {
                name = path;
                string directory = path + Path.DirectorySeparatorChar;

                long running = 0;
                foreach (string file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    string f = file.Substring(directory.Length);

                    if (f.StartsWith("."))
                        continue;

                    long size = new FileInfo(file).Length;

                    files.Add(new FileItem()
                    {
                        Path = f,
                        Size = size,
                        Offset = running
                    });

                    running += size;
                }
            }

            Torrent torrent = new Torrent(name, "", files, trackers, pieceSize);
            torrent.Comment = comment;
            torrent.CreatedBy = "TestClient";
            torrent.CreationDate = DateTime.Now;
            torrent.Encoding = Encoding.UTF8;

            return torrent;
        }

        public void UpdatedTrackers(TrackerEvent ev,string id,int port)
        {
            foreach (var tracker in Trackers)
                tracker.Update(this, ev, id, port);
        }

        public void ResetTrackersLastRequest()
        {
            foreach (var tracker in Trackers)
                tracker.ResetLastRequest();
        }

        public void HandlePeerListUpdated(object sender,List<IPEndPoint> endPoints) 
        {
            var handler = this.PeerListUpdated;
            if (handler != null)
                handler(sender, endPoints);
        }
    }
}
