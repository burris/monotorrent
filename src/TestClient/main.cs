using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;
using MonoTorrent.Common;
using MonoTorrent.Client;
using System.Net;
using System.Diagnostics;
using System.Threading;
using MonoTorrent.Client.PeerMessages;

namespace TestClient
{
    class main
    {
        static string basePath;
        static ClientEngine engine;
        static List<ITorrentManager> torrents = new List<ITorrentManager>();

        static void Main(string[] args)
        {
            basePath = Environment.CurrentDirectory;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(Console_CancelKeyPress);

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            Thread.GetDomain().UnhandledException += new UnhandledExceptionEventHandler(UnhandledException);
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
            Debug.Listeners.Clear();
            Debug.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out));

            Debug.Flush();
            TestEngine();
        }

        static private void TestEngine()
        {
            engine = new ClientEngine(EngineSettings.DefaultSettings, TorrentSettings.DefaultSettings);
            engine.Settings.DefaultSavePath = Path.Combine(basePath, "Downloads");

            if (!Directory.Exists(engine.Settings.DefaultSavePath))
                Directory.CreateDirectory(engine.Settings.DefaultSavePath);

            if (!Directory.Exists(Path.Combine(basePath, "Torrents")))
                Directory.CreateDirectory(Path.Combine(basePath, "Torrents"));

            foreach (string file in Directory.GetFiles(Path.Combine(basePath, "Torrents")))
            {
                if (file.EndsWith(".torrent"))
                    torrents.Add(engine.LoadTorrent(file));
            }

            if (torrents.Count == 0)
            {
                Console.WriteLine("No torrents found in the Torrents directory");
                Console.WriteLine("Exiting...");
                return;
            }
            Debug.WriteLine("Torrent State:    " + ((TorrentManager)torrents[0]).State.ToString());
            foreach (TorrentManager manager in torrents)
            {
                engine.Start(manager);
                manager.OnPieceHashed += new EventHandler<PieceHashedEventArgs>(main_OnPieceHashed);
                manager.OnTorrentStateChanged += new EventHandler<TorrentStateChangedEventArgs>(main_OnTorrentStateChanged);
            }


            int i = 0;
            bool running = true;
            while (running)
            {
                if ((i++) % 4 == 0)
                {
                    running = false;
                    Console.Clear();
                    foreach (TorrentManager manager in torrents)
                    {
                        if (manager.State != TorrentState.Stopped)
                            running = true;

                        Debug.WriteLine("Torrent:          " + manager.Torrent.Name);
                        Debug.WriteLine("Uploading to:     " + manager.UploadingTo.ToString());
                        Debug.WriteLine("Half opens:       " + ClientEngine.connectionManager.HalfOpenConnections);
                        Debug.WriteLine("Max open:         " + ClientEngine.connectionManager.MaxOpenConnections);
                        Debug.WriteLine("Progress:         " + string.Format(manager.Progress().ToString(), ("{0:0.00}")));
                        Debug.WriteLine("Download Speed:   " + string.Format("{0:0.00}", manager.DownloadSpeed() / 1024));
                        Debug.WriteLine("Upload Speed:     " + string.Format("{0:0.00}", manager.UploadSpeed() / 1024));
                        Debug.WriteLine("Torrent State:    " + manager.State.ToString());
                        Debug.WriteLine("Number of seeds:  " + manager.Seeds());
                        Debug.WriteLine("Number of leechs: " + manager.Leechs());
                        Debug.WriteLine("Total available:  " + manager.AvailablePeers);
                        Debug.WriteLine("Downloaded:       " + manager.BytesDownloaded / 1024);
                        Debug.WriteLine("Uploaded:         " + manager.BytesUploaded / 1024);
                        Debug.WriteLine("\n");
                    }
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        #region Shutdown methods
        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            shutdown();
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Unhandled exception");
            WaitHandle[] handles = engine.Stop();
            WaitHandle.WaitAll(handles);
            Console.WriteLine(e.ExceptionObject.ToString());
        }

        static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Unhandled");
            Debug.WriteLine(sender.ToString());
        }

        static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            shutdown();
        }

        private static void shutdown()
        {
#warning Maybe return a wait handle and wait for the response.
            WaitHandle[] handles = engine.Stop();
            int a = Environment.TickCount;
            WaitHandle.WaitAll(handles);
            Console.WriteLine(Environment.TickCount - a);
            Console.ReadLine(); Console.ReadLine(); Console.ReadLine(); Console.ReadLine();

            foreach (TraceListener lst in Debug.Listeners)
            {
                lst.Flush();
                lst.Close();
            }
        }
        #endregion


        #region events i've hooked into
        static void main_OnTorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
        {
            Debug.WriteLine("State: " + e.NewState.ToString());
        }

        static void main_OnPieceHashed(object sender, PieceHashedEventArgs e)
        {
            ITorrentManager manager = (ITorrentManager)sender;
            if (e.HashPassed)
                Debug.WriteLine("Hash Passed: " + manager.Torrent.Name + " " + e.PieceIndex);
            else
                Debug.WriteLine("Hash Failed: " + manager.Torrent.Name + " " + e.PieceIndex);
        }
        #endregion
    }
}
