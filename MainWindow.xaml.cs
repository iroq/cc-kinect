using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;

namespace CC.Kinect
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Diagnostics;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Bitmap that will hold color information
        /// </summary>
        private WriteableBitmap colorBitmap;

        /// <summary>
        /// Intermediate storage for the depth data received from the camera
        /// </summary>
        private DepthImagePixel[] depthPixels;

        short[,] diffArray;
        /// <summary>
        /// Intermediate storage for the depth data converted to color
        /// </summary>
        private byte[] colorPixels;

        private const short depthDelta = 40;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the depth stream to receive depth frames
                this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                
                // Allocate space to put the depth pixels we'll receive
                this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];

                // Allocate space to put the color pixels we'll create
                this.colorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];

                // This is the bitmap we'll display on-screen
                this.colorBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
                
                // Set the image we display to point to the bitmap where we'll put the image data
                this.Image.Source = this.colorBitmap;

                // Add an event handler to be called whenever there is new depth frame data
                this.sensor.DepthFrameReady += this.SensorDepthFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }


        bool frameProcessing;

        /// <summary>
        /// Event handler for Kinect sensor's DepthFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            if (!frameProcessing)
            {
                using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
                {
					if (depthFrame != null)
					{
						if (diffArray == null)
							diffArray = new short[depthFrame.Width, depthFrame.Height];
						depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
					    frameProcessing = true;
						this.Dispatcher.BeginInvoke(DispatcherPriority.Background,
							(Action) (() => this.ProcessDepthData(depthFrame)));
					}
                }
            }
        }

        private void ProcessDepthData(DepthImageFrame frame)
        {
            var minDepth = frame.MinDepth;
            var maxDepth = frame.MaxDepth;

            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    diffArray[i, j] = depthPixels[i * frame.Height + j].Depth;
                    if (diffArray[i, j] < minDepth)
                        diffArray[i, j] = 0;
                    else if (diffArray[i, j] > maxDepth)
                        diffArray[i, j] = short.MaxValue;
                }
            }

            int dx;
            int dy;

            //Calculate diffs
            //for (int i = 0; i < frame.Width - 1; i++)
            //    for (int j = 0; j < frame.Height - 1; j++)
            //    {
            //        dx = diffArray[i, j] - diffArray[i + 1, j];
            //        dy = diffArray[i, j] - diffArray[i, j + 1];

            //        diffArray[i, j] = (short)(dx);
            //    }

            // Convert the depth to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    // Write out blue byte
                    this.colorPixels[colorPixelIndex++] = 0;

                    // Write out green byte
                    this.colorPixels[colorPixelIndex++] = 0;

                    if (Math.Abs(diffArray[i, j]) >= 1000 && Math.Abs(diffArray[i,j])<=2000)
                    {
                        // Write out red byte                        
                        this.colorPixels[colorPixelIndex++] = 255;
                    }
                    else
                    {
                        // Write out red byte                        
                        this.colorPixels[colorPixelIndex++] = 0;
                    }

                    // We're outputting BGR, the last byte in the 32 bits is unused so skip it
                    // If we were outputting BGRA, we would write alpha here.
                    ++colorPixelIndex;
                }

            }

            // Write the pixel data into our bitmap
            this.colorBitmap.WritePixels(
                new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight),
                this.colorPixels,
                this.colorBitmap.PixelWidth * sizeof(int),
                0);
            Point pos = Mouse.GetPosition(Image);
            updateDataLabel((int)pos.X, (int)pos.Y);
            frameProcessing = false;
        }

        /// <summary>
        /// Retrieves appropriate value from the frame and displays it in the 
        /// DataLabel. Arguments are position relative to the image control.
        /// </summary>
        private void updateDataLabel(int scrX, int scrY)
        {
            if (scrX >= 0 && scrX < Image.Width &&
                scrY >= 0 && scrY < Image.Height)
            {
                int x = (int)((scrX / (double)Image.Width) * diffArray.GetLength(0));
                int y = (int)((scrY / (double)Image.Height) * diffArray.GetLength(1));
                dataLabel.Content = "arr["+x+","+y+"]= "+diffArray[x, y].ToString();
            }
        }

        private void Image_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Point pos=e.GetPosition(Image);
            updateDataLabel((int)pos.X, (int)pos.Y);
        }
    }
}