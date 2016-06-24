using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Net;
using System.IO;
using System.Timers;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Collections.Generic;

namespace KiepWebcam
{
    public partial class Viewer : Window
    {
        private const string CACHEFILE = "Webcam.jpg";
        private const string LOGFILE = "KiepWebcam.log";
        private int[] CATCH_KEYCODES = { 107, 111 };
        private const string URL = "https://vid.nl/ImageCamera/cam_27";

        private delegate void DummyDelegate();

        private class DownloadedImage
        {
            public DateTime lastModified;
            public byte[] data;
        }

        public Viewer()
        {
            InitializeComponent();

            // Cannot debug when application has topmost
#if !DEBUG
            this.Topmost = true;
#endif

            // Wait 1 second before subscribing to keypresses
            WaitSubscribeKeypresses();

            // Log startup
            Log("Start");

            // Apply rotation animation to status image
            DoubleAnimation da = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3)));
            RotateTransform rt = new RotateTransform();
            imgStatus.RenderTransform = rt;
            imgStatus.RenderTransformOrigin = new Point(0.5, 0.5);
            da.RepeatBehavior = RepeatBehavior.Forever;
            rt.BeginAnimation(RotateTransform.AngleProperty, da);

            // Show image
            TryReadFromFile(CACHEFILE);
            TryReadFromWeb(URL);
        }

        #region Keyboard and mouse handling
        private void WaitSubscribeKeypresses()
        {
            try
            {
                Timer timer = new Timer(1000);
                timer.Elapsed += delegate
                {
                    this.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    (DummyDelegate)
                    delegate 
                    {
                        timer.Enabled = false;

                        // Block keys from being received by other applications
                        List<int> blockedKeys = new List<int>(CATCH_KEYCODES);
                        LowLevelKeyboardHook.Instance.SetBlockedKeys(blockedKeys);

                        // Subscribe to low level keypress events
                        LowLevelKeyboardHook.Instance.KeyboardHookEvent += new LowLevelKeyboardHook.KeyboardHookEventHandler(Instance_KeyboardHookEvent);
                    });
                };
                timer.Enabled = true;
            }
            catch (Exception ex) 
            {
                Log("Error attaching keyboard hook\t" + ex.Message);
            }
        }

        void Instance_KeyboardHookEvent(int keycode)
        {
            if (new List<int>(CATCH_KEYCODES).Contains(keycode))
            {
                Log("Keypress\t" + keycode);
                Click();
            }
        }

        private void MouseDownHandler(object sender, MouseButtonEventArgs e)
        {
            Log("MouseClick");
            Click();
        }

        private void Click()
        {
            Log("Exit");
            Application.Current.Shutdown();
        }
        #endregion

        #region Read and write file
        private string TryReadFromFile(string filename)
        {
            string result = "";
            try
            {
                string baseDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                string filenameandpath = baseDir + "\\" + filename;
                if (File.Exists(filenameandpath))
                {
                    DownloadedImage downloadedImage = new DownloadedImage();
                    downloadedImage.data = File.ReadAllBytes(filenameandpath);

                    try
                    {
                        downloadedImage.lastModified = File.GetLastWriteTime(filenameandpath);
                    }
                    catch (Exception ex) 
                    {
                        Log("Error setting LastModified from file\t" + ex.Message);
                    }

                    ShowImageFromBuffer(downloadedImage);
                }
            }
            catch (Exception ex) 
            {
                Log("Error loading image from file\t" + ex.Message);
            }

            return result;
        }

        private void WriteToFile(string filename, DownloadedImage downloadedImage)
        {
            try
            {
                if (downloadedImage != null && downloadedImage.data != null && downloadedImage.data.Length > 0)
                {
                    string baseDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    FileStream fileStream = new FileStream(baseDir + "\\" + filename, FileMode.Create, FileAccess.ReadWrite);
                    BinaryWriter fileWriter = new BinaryWriter(fileStream);
                    fileWriter.Write(downloadedImage.data);
                    fileWriter.Close();

                    if (downloadedImage.lastModified != DateTime.MinValue)
                    {
                        File.SetLastWriteTime(baseDir + "\\" + filename, downloadedImage.lastModified);
                    }
                }
            }
            catch (Exception ex) 
            {
                Log("Error writing image to file\t" + ex.Message);
            }
        }
        #endregion

        #region Read from web (asynchronous)
        private void TryReadFromWeb(string url)
        {
            try
            {
                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(bw_DoWork);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);
                bw.RunWorkerAsync(url);
            }
            catch (Exception ex) 
            {
                Log("Error downloading image\t" + ex.Message);
            }
        }

        void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = null;
            try
            {
                string url = (string)e.Argument;
                WebClient webClient = new WebClient();
                DownloadedImage downloadedImage = new DownloadedImage();

                downloadedImage.data = webClient.DownloadData(url);

                // Try to get last modified date+time
                try
                {
                    string lastmodified = webClient.ResponseHeaders["Last-Modified"];
                    downloadedImage.lastModified = DateTime.Parse(lastmodified);
                }
                catch (Exception ex) 
                {
                    Log("Error setting LastModified from download\t" + ex.Message);
                }

                e.Result = downloadedImage;
            }
            catch (Exception ex) 
            {
                Log("Error loading image\t" + ex.Message);
            }
        }

        void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            DownloadedImage result = (DownloadedImage)e.Result;
            if (result != null && result.data != null && result.data.Length > 0)
            {
                WriteToFile(CACHEFILE, result);
                ShowImageFromBuffer(result);
                imgStatus.Source = new BitmapImage(new Uri("Ok.png", UriKind.Relative));
                imgStatus.RenderTransform = null;
            }
            else
            {
                imgStatus.Source = new BitmapImage(new Uri("Error.png", UriKind.Relative));
                imgStatus.RenderTransform = null;
            }
        }
        #endregion

        #region Show image
        private void ShowImageFromBuffer(DownloadedImage downloadedImage)
        {
            try
            {
                if (downloadedImage != null && downloadedImage.data != null && downloadedImage.data.Length > 0)
                {
                    MemoryStream stream = new MemoryStream(downloadedImage.data);
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.EndInit();
                    imgWebcam.Source = image;

                    // Show last modified date+time
                    if (downloadedImage.lastModified != DateTime.MinValue)
                    {
                        lblLastModified.Content = downloadedImage.lastModified.ToString("dddd d MMMM  H:mm");
                    }
                }
            }
            catch (Exception ex) 
            {
                Log("Error showing image\t" + ex.Message);
            }
        }
        #endregion

        private void Log(string text)
        {
            try
            {
                if (text != "")
                {
                    string baseDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    StreamWriter cachefile = File.AppendText(baseDir + "\\" + LOGFILE);
                    cachefile.WriteLine(DateTime.Now + "\t" + text);
                    cachefile.Close();
                }
            }
            catch (Exception) { }
        }
    }
}
