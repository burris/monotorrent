//
// TorrentCreator.cs
//
// Authors:
//   Gregor Burger burger.gregor@gmail.com
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006-2007 Gregor Burger and Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//



using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using MonoTorrent.BEncoding;
using System.Threading;
using MonoTorrent.Client;


namespace MonoTorrent.Common
{
    public class TorrentCreator
    {
        #region Events

        /// <summary>
        /// This event indicates the progress of the torrent creation and is fired every time a piece is hashed
        /// </summary>
        public event EventHandler<TorrentCreatorEventArgs> Hashed;

        #endregion


        #region Private Fields

        private List<MonoTorrentCollection<string>> announces;      // The list of announce urls
        private bool ignoreHiddenFiles;                             // True if you want to ignore hidden files when making the torrent
        private string path;                                        // The path from which the torrent will be created (can be file or directory)
        private TorrentCreatorAsyncResult result;                   // The IAsyncResult generated by creating the torrent
        private bool storeMd5;                                      // True if an MD5 hash of each file should be included
        private BEncodedDictionary torrent;                         // The BencodedDictionary which contains the data to be written to the .torrent file

        #endregion Private Fields


        #region Properties

        public List<MonoTorrentCollection<string>> Announces
        {
            get { return this.announces; }
        }

        public string Comment
        {
            get
            {
                BEncodedValue val = Get(this.torrent, new BEncodedString("comment"));
                return val == null ? string.Empty : val.ToString();
            }
            set { Set(this.torrent, "comment", new BEncodedString(value)); }
        }

        public string CreatedBy
        {
            get
            {
                BEncodedValue val = Get(this.torrent, new BEncodedString("created by"));
                return val == null ? string.Empty : val.ToString();
            }
            set { Set(this.torrent, "created by", new BEncodedString(value)); }
        }

        public string Encoding
        {
            get { return Get(this.torrent, new BEncodedString("encoding")).ToString(); }
        }

        public bool IgnoreHiddenFiles
        {
            get { return ignoreHiddenFiles; }
            set { ignoreHiddenFiles = value; }
        }

        /// <summary>
        /// The path from which the torrent can be created. This can be either
        /// a file or a folder containing the files to hash.
        /// </summary>
        public string Path
        {
            get { return path; }
            set { path = value; }

        }

        ///<summary>
        /// The length of each piece in bytes (range 16384 bytes -> 4MB)
        ///</summary>
        public long PieceLength
        {
            get
            {
                BEncodedValue val = Get((BEncodedDictionary)this.torrent["info"], new BEncodedString("piece length"));
                return val == null ? -1 : ((BEncodedNumber)val).Number;
            }
            set { Set((BEncodedDictionary)this.torrent["info"], "piece length", new BEncodedNumber(value)); }
        }

        ///<summary>
        /// A private torrent can only accept peers from the tracker and will not share peer data
        /// through DHT
        ///</summary>
        public bool Private
        {
            get
            {
                BEncodedValue val = Get((BEncodedDictionary)this.torrent["info"], new BEncodedString("private"));
                return val == null ? false : ((BEncodedNumber)val).Number == 1;
            }
            set { Set((BEncodedDictionary)this.torrent["info"], "private", new BEncodedNumber(value ? 1 : 0)); }
        }

        public string Publisher
        {
            get
            {
                BEncodedValue val = Get(this.torrent, new BEncodedString("publisher"));
                return val == null ? string.Empty : val.ToString();
            }
            set { Set(this.torrent, "publisher", new BEncodedString(value)); }
        }

        public string PublisherUrl
        {
            get
            {
                BEncodedValue val = Get(this.torrent, new BEncodedString("publisher-url"));
                return val == null ? string.Empty : val.ToString();
            }
            set { Set(this.torrent, "publisher-url", new BEncodedString(value)); }
        }

        public bool StoreMD5
        {
            get { return storeMd5; }
            set { storeMd5 = value; }
        }

        #endregion Properties


        #region Constructors

        public TorrentCreator()
        {
            BEncodedDictionary info = new BEncodedDictionary();
            this.announces = new List<MonoTorrentCollection<string>>();
            this.ignoreHiddenFiles = true;
            this.torrent = new BEncodedDictionary();
            this.torrent.Add("info", info);

            // Add in initial values for some of the torrent attributes
            PieceLength = 256 * 1024;   // 256kB default piece size
            torrent.Add("encoding", (BEncodedString)"UTF-8");
        }

        #endregion Constructors


        #region Public Methods

        /// <summary>
        /// Adds a custom value to the main bencoded dictionary
        /// </summary>        
        public void AddCustom(BEncodedString key, BEncodedValue value)
        {
            this.torrent.Add(key, value);
        }

        public TorrentCreatorAsyncResult BeginCreate(object asyncState, AsyncCallback callback)
        {
            if (result != null)
                throw new TorrentException("You must call EndCreate before calling BeginCreate again");

            result = new TorrentCreatorAsyncResult(asyncState, callback);

            // Start the processing in a seperate thread, a user thread.
            new Thread(new ThreadStart(AsyncCreate)).Start();
            return result;
        }

        /// <summary>
        /// Creates a Torrent and returns it in it's dictionary form
        /// </summary>
        /// <returns></returns>
        public BEncodedDictionary Create()
        {
            Reset();
            CreateDict();
            return BEncodedValue.Decode<BEncodedDictionary>(this.torrent.Encode());
        }

        ///<summary>
        /// Creates a Torrent and writes it to disk in the specified location
        ///<summary>
        ///<param name="storagePath">The path (including filename) where the new Torrent will be written to</param>
        public void Create(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");

            using (FileStream stream = new FileStream(path, FileMode.Create))
                Create(stream);
        }

        /// <summary>
        /// Generates a Torrent and writes it to the supplied stream
        /// </summary>
        /// <param name="stream">The stream to write the torrent to</param>
        public void Create(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            Create();

            byte[] data = this.torrent.Encode();
            stream.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Ends the asynchronous torrent creation and returns the torrent in it's dictionary form
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public BEncodedDictionary EndCreate(IAsyncResult result)
        {
            if (result == null)
                throw new ArgumentNullException("result");

            if (result != this.result)
                throw new ArgumentException("The supplied async result does not correspond to currently active async result");

            try
            {
                if(!result.IsCompleted)
                    result.AsyncWaitHandle.WaitOne();

                if (this.result.SavedException != null)
                    throw this.result.SavedException;

                return this.result.Aborted ? null : BEncodedValue.Decode<BEncodedDictionary>(this.torrent.Encode());
            }
            finally
            {
                this.result = null;
            }
        }

        /// <summary>
        /// Ends the asynchronous torrent creation and writes the torrent to the specified path
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public void EndCreate(IAsyncResult result, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");

            using (FileStream s = File.OpenWrite(path))
                EndCreate(result, s);
        }

        /// <summary>
        /// Ends the asynchronous torrent creation and writes the torrent to the specified stream
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public void EndCreate(IAsyncResult result, Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            byte[] data = EndCreate(result).Encode();
            stream.Write(data, 0, data.Length);
        }

        ///<summary>
        /// Calculates the approximate size of the final .torrent in bytes
        ///</summary>
        public long GetSize()
        {
            MonoTorrentCollection<string> paths = new MonoTorrentCollection<string>();

            if (Directory.Exists(this.path))
                GetAllFilePaths(this.path, paths);
            else
                paths.Add(path);

            long size = 0;
            for (int i = 0; i < paths.Count; i++)
                size += new FileInfo(paths[i]).Length;

            return size;
        }

        public int RecommendedPieceSize()
        {
            long totalSize = GetSize();
            
            // Check all piece sizes that are multiples of 32kB 
            for (int i = 32768; i < 4 * 1024 * 1024; i *= 2)
            {
                int pieces = (int)(totalSize / i) + 1;
                if ((pieces * 20) < (60 * 1024))
                    return i;
            }

            // If we get here, we're hashing a massive file, so lets limit
            // to a max of 4MB pieces.
            return 4 * 1024 * 1024;
        }

        /// <summary>
        /// Removes a custom value from the main bencoded dictionary.
        /// </summary>
        public void RemoveCustom(BEncodedString key)
        {
            this.torrent.Remove(key);
        }

        #endregion Public Methods


        #region Private Methods

        ///<summary>
        ///this method runs recursively through all subdirs under dir and ads information
        ///from each file to the filesList. 
        ///<summary>
        ///<param name="dir">the top directory to start from</param>
        ///<param name="filesList">the list to store the file information</param>
        ///<param name="paths">a list of all files found. used for piece hashing later</param>
        private void AddAllFileInfoDicts(string dir, BEncodedList filesList, MonoTorrentCollection<string> paths)
        {
            GetAllFilePaths(dir, paths);

            for (int i = 0; i < paths.Count; i++)
                filesList.Add(GetFileInfoDict(paths[i], dir));
        }

        private void AsyncCreate()
        {
            try
            {
                Reset();
                CreateDict();
            }
            catch (Exception ex)
            {
                result.SavedException = ex;
            }

            result.IsCompleted = true;
            ((ManualResetEvent)result.AsyncWaitHandle).Set();

            if (result.Callback != null)
                result.Callback(this.result);
        }

        private void AsyncRaiseHashed(object e)
        {
            if (Hashed != null)
                Hashed(this, (TorrentCreatorEventArgs)e);
        }

        ///<summary>
        ///this adds stuff common to single and multi file torrents
        ///</summary>
        private void AddCommonStuff(BEncodedDictionary torrent)
        {
            Logger.Log(null, announces[0][0]);
            torrent.Add("announce", new BEncodedString(announces[0][0]));

            // If there is more than one tier or the first tier has more than 1 tracker
            if (announces.Count > 1 || announces[0].Count > 0)
            {
                BEncodedList announceList = new BEncodedList();
                for (int i = 0; i < this.announces.Count; i++)
                {
                    BEncodedList tier = new BEncodedList();
                    for (int j = 0; j < this.announces[i].Count; j++)
                        tier.Add(new BEncodedString(this.announces[i][j]));

                    announceList.Add(tier);
                }

                torrent.Add("announce-list", announceList);
            }


            DateTime epocheStart = new DateTime(1970, 1, 1);
            TimeSpan span = DateTime.Now - epocheStart;
			Logger.Log(null, "creation date: {0} - {1} = {2}:{3}", DateTime.Now, epocheStart, span, span.TotalSeconds);
            torrent.Add("creation date", new BEncodedNumber((long)span.TotalSeconds));
        }

        ///<summary>calculate md5sum of a file</summary>
        ///<param name="fileName">the file to sum with md5</param>
        private void AddMD5(BEncodedDictionary dict, string fileName)
        {
            MD5 hasher = MD5.Create();
            StringBuilder sb = new StringBuilder();

            using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = hasher.ComputeHash(stream);

                foreach (byte b in hash)
                {
                    string hex = b.ToString("X");
                    hex = hex.Length > 1 ? hex : "0" + hex;
                    sb.Append(hex);
                }
				Logger.Log(null, "Sum for: '{0}' = {1}", fileName, sb.ToString());
            }
            dict.Add("md5sum", new BEncodedString(sb.ToString()));
        }

        ///<summary>
        ///calculates all hashes over the files which should be included in the torrent
        ///</summmary>
        private byte[] CalcPiecesHash(MonoTorrentCollection<string> fullPaths)
        {
            SHA1 hasher = SHA1.Create();
            byte[] piece = new byte[PieceLength];//holds one piece for sha1 calcing
            int len = 0;        //holds the bytes read by the stream.Read() method
            byte[] piecesBuffer = new byte[GetPieceCount(fullPaths) * 20]; //holds all the pieces hashes
            int piecesBufferOffset = 0;

            using (CatStreamReader reader = new CatStreamReader(fullPaths))
            {
                //go through each path added earlier by AddAllInfoDicts
                len = reader.Read(piece, 0, piece.Length);
                while (len != 0)
                {
                    // If we are using the synchronous version, result is null
                    if (result != null && result.Aborted)
                        return piecesBuffer;

                    byte[] currentHash = hasher.ComputeHash(piece, 0, len);
                    RaiseHashed(new TorrentCreatorEventArgs(reader.CurrentFile.Position, reader.CurrentFile.Length,
                                                            piecesBufferOffset * PieceLength, (piecesBuffer.Length - 20) * PieceLength));
                    Buffer.BlockCopy(currentHash, 0, piecesBuffer, piecesBufferOffset, currentHash.Length);
                    piecesBufferOffset += currentHash.Length;
                    len = reader.Read(piece, 0, piece.Length);
                }
            }
            return piecesBuffer;
        }

        ///<summary>
        ///creates an BencodedDictionary.
        ///</summary>
        ///<returns>a BDictionary representing the torrent</returns>
        private void CreateDict()
        {
            if (Directory.Exists(Path))
            {
				Logger.Log(null, "Creating multifile torrent from: {0}", Path);
                CreateMultiFileTorrent();
                return;
            }
            if (File.Exists(Path))
            {
				Logger.Log(null, "Creating singlefile torrent from: {0}", Path);
                CreateSingleFileTorrent();
                return;
            }

            throw new ArgumentException("no such file or directory", "storagePath");
        }

        ///<summary>
        ///used for creating multi file mode torrents.
        ///</summary>
        ///<returns>the dictionary representing which is stored in the torrent file</returns>
        protected void CreateMultiFileTorrent()
        {
            AddCommonStuff(this.torrent);
            BEncodedDictionary info = (BEncodedDictionary)this.torrent["info"];

            MonoTorrentCollection<string> fullPaths = new MonoTorrentCollection<string>();//store files to hash over
            BEncodedList files = new BEncodedList();//the dict which hold the file infos


            AddAllFileInfoDicts(Path, files, fullPaths);//do recursively

            info.Add("files", files);

            string name = GetDirName(Path);

			Logger.Log(null, "Topmost directory: {0}", name);
            info.Add("name", new BEncodedString(name));

            info.Add("pieces", new BEncodedString(CalcPiecesHash(fullPaths)));
        }

        ///<summary>
        ///used for creating a single file torrent file
        ///<summary>
        ///<returns>the dictionary representing which is stored in the torrent file</returns>
        protected void CreateSingleFileTorrent()
        {
            AddCommonStuff(this.torrent);

            BEncodedDictionary infoDict = (BEncodedDictionary)this.torrent["info"];

            infoDict.Add("length", new BEncodedNumber(new FileInfo(Path).Length));
            if (StoreMD5)
                AddMD5(infoDict, Path);

            infoDict.Add("name", new BEncodedString(System.IO.Path.GetFileName(Path)));
			Logger.Log(null, "name == {0}", System.IO.Path.GetFileName(Path));
            MonoTorrentCollection<string> files = new MonoTorrentCollection<string>();
            files.Add(Path);
            infoDict.Add("pieces", new BEncodedString(CalcPiecesHash(files)));
        }

        private static BEncodedValue Get(BEncodedDictionary dictionary, BEncodedString key)
        {
            return dictionary.ContainsKey(key) ? dictionary[key] : null;
        }

        private void GetAllFilePaths(string directory, MonoTorrentCollection<string> paths)
        {
            string[] subs = Directory.GetFileSystemEntries(directory);
            foreach (string path in subs)
            {
                if (Directory.Exists(path))
                {
                    if (ignoreHiddenFiles)
                    {
                        DirectoryInfo info = new DirectoryInfo(path);
                        if ((info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                            continue;
                    }

                    GetAllFilePaths(path, paths);
                }
                else
                {
                    if (ignoreHiddenFiles)
                    {
                        FileInfo info = new FileInfo(path);
                        if ((info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                            continue;
                    }

                    paths.Add(path);
                }
            }
        }

        private string GetDirName(string path)
        {
            string[] pathEntries = path.Split(new char[] { System.IO.Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            int i = pathEntries.Length - 1;
            while (String.IsNullOrEmpty(pathEntries[i]))
                i--;
            return pathEntries[i];
        }

        ///<summary>
        ///this method is used for multi file mode torrents to return a dictionary with
        ///file relevant informations. 
        ///<param name="file">the file to report the informations for</param>
        ///<param name="basePath">used to subtract the absolut path information</param>
        ///</summary>
        private BEncodedDictionary GetFileInfoDict(string file, string basePath)
        {
            BEncodedDictionary fileDict = new BEncodedDictionary();

            fileDict.Add("length", new BEncodedNumber(new FileInfo(file).Length));

            if (StoreMD5)
                AddMD5(fileDict, file);

            file = file.Remove(0, Path.Length);
			Logger.Log(null, "Without base[{0}]: {1}", basePath, file);
            BEncodedList path = new BEncodedList();
            string[] splittetPath = file.Split(System.IO.Path.DirectorySeparatorChar);

            foreach (string s in splittetPath)
            {
                if (s.Length > 0)//exclude empties
                    path.Add(new BEncodedString(s));
            }

            fileDict.Add("path", path);

            return fileDict;
        }

        private long GetPieceCount(MonoTorrentCollection<string> fullPaths)
        {
            long size = 0;
            foreach (string file in fullPaths)
                size += new FileInfo(file).Length;

            //double count = (double)size/PieceLength;
            long pieceCount = size / PieceLength + (((size % PieceLength) != 0) ? 1 : 0);
			Logger.Log(null, "Piece Count: {0}", pieceCount);
            return pieceCount;
        }

        private void RaiseHashed(TorrentCreatorEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(AsyncRaiseHashed, e);
        }

        private void Reset()
        {

            BEncodedDictionary oldInfo = (BEncodedDictionary)this.torrent["info"];
            try
            {
                this.torrent.Remove("info");
                this.torrent.Remove("announce");
                this.torrent.Remove("announce-list");
                this.torrent.Remove("creation date");
            }
            finally
            {
                this.torrent.Add("info", new BEncodedDictionary());
                ((BEncodedDictionary)this.torrent["info"]).Add("piece length", oldInfo["piece length"]);

                //restore private value
                if (oldInfo.ContainsKey("private"))
                    ((BEncodedDictionary)this.torrent["info"]).Add("private", oldInfo["private"]);
            }
        }

        private static void Set(BEncodedDictionary dictionary, BEncodedString key, BEncodedValue value)
        {
            if (dictionary.ContainsKey(key))
                dictionary[key] = value;
            else
                dictionary.Add(key, value);
        }

        #endregion Private Methods
    }


    ///<summary>
    ///this class is used to concatenate all the files provided as parameter from the constructor.
    ///<summary>
    internal class CatStreamReader : IDisposable
    {
        private MonoTorrentCollection<string> _files;
        private int _currentFile = 0;
        private FileStream _currentStream = null;

        public FileStream CurrentFile
        {
            get { return _currentStream; }
        }

        public CatStreamReader(MonoTorrentCollection<string> files)
        {
            _files = files;
            _currentStream = new FileStream(files[_currentFile++], FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public int Read(byte[] data, int offset, int count)
        {
            int len = 0;

            while (len != count)
            {
                int tmp = _currentStream.Read(data, offset + len, count - len);
                len += tmp;
                if (tmp == 0)
                {
                    if (_currentFile == _files.Count)
                        return len;

                    _currentStream.Close();
                    _currentStream.Dispose();
                    _currentStream = new FileStream(_files[_currentFile++], FileMode.Open, FileAccess.Read, FileShare.Read);
                }
            }
            return len;
        }

        public void Dispose()
        {
            _currentStream.Dispose();
        }
    }
}
