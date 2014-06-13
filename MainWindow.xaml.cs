using System.Collections.Generic;
using System.Threading;
using System.Windows.Documents;

namespace CC.Kinect
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;

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

        /// <summary>
        /// Intermediate storage for the depth data converted to color
        /// </summary>
        private byte[] colorPixels;

        private const short depthDelta = 4;

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

        /// <summary>
        /// Event handler for Kinect sensor's DepthFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
                    // Get the min and max reliable depth for the current frame
                    short[,] depthArray = new short[depthFrame.Width, depthFrame.Height];
                    for (int i = 0; i < depthFrame.Width; i++)
                    {
                        for (int j = 0; j < depthFrame.Height; j++)
                        {
                            depthArray[i,j] = depthPixels[i * depthFrame.Height + j].Depth;
                        }
                    }

                    var depthArray2 = (short[,])depthArray.Clone();

                    int minDepth = depthFrame.MinDepth;
                    int maxDepth = depthFrame.MaxDepth;

                    // Convert the depth to RGB
                    int colorPixelIndex = 0;
                    for (int i = 0; i < depthFrame.Width - 1; ++i)
                    {
                        for (int j = 0; j < depthFrame.Height - 1; j++)
                        {
                            short oldDepth = depthArray[i, j];

                            if (oldDepth <= minDepth || oldDepth >= maxDepth)
                                depthArray[i, j] = 0;

                            short dx = (short)(depthArray[i, j] - depthArray[i + 1, j]);
                            short dy = (short)(depthArray[i, j] - depthArray[i, j + 1]);

                            depthArray2[i, j] = (short)(dx + dy);
                        }
                    }

                    List<GraphicalObject> graphicalObjects = DivideGraphicalObjects(depthArray, depthArray2);

                    this.colorBitmap.WritePixels(
                        new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight),
                        this.colorPixels,
                        this.colorBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }
        }

        private List<GraphicalObject> DivideGraphicalObjects(short[,] depthArray, short[,] depthArray2)
        {
            int width = depthArray.GetLength(0);
            int height = depthArray.GetLength(1);
            List<GraphicalObject> graphicalObjects = new List<GraphicalObject>();
            var currentGraphicalObject = new GraphicalObject();
            for (int i = 0; i < depthArray.GetLength(0); i++)
            {
                for (int j = 0; j < depthArray.GetLength(1); j++)
                {
                    if (Math.Abs(depthArray2[i, j] - depthArray2[i + 1, j]) < depthDelta)
                    {
                        currentGraphicalObject.GraphicalPoints.Add(new GraphicalPoint()
                        {
                            X = i,
                            Y = j,
                            Depth = depthArray[i, j]
                        });
                    }
                    else
                    {
                        
                    }
                }
            }
        }

        private short GetDepthFromBottomPixel(DepthImagePixel[] depthImagePixels, int i, int width)
        {
            int below = GetBottomIndex(i, width, depthImagePixels.Length);
            return depthImagePixels[below].Depth;
        }

        private int GetBottomIndex(int i, int width, int length)
        {
            int row = i / width;
            int column = i % width;
            int below = (row + 1) * width + column;
            if (below < 0 || below >= length)
                return -1;
            return below;
        }

        private short GetDepthFromTopPixel(DepthImagePixel[] depthImagePixels, int i, int width)
        {
            int above = GetTopIndex(i, width, depthImagePixels.Length);
            return depthImagePixels[above].Depth;
        }

        private int GetTopIndex(int i, int width, int length)
        {
            int row = i / width;
            int column = i % width;
            int above = (row - 1) * width + column;
            if (above < 0 || above >= length)
                return -1;
            return above;
        }

        private short GetDepthFromLeftPixel(DepthImagePixel[] depthImagePixels, int i, int width)
        {
            int row = i / width;
            int column = i % width;
            int left = row * width + column - 1;
            if (left < 0 || left >= depthImagePixels.Length)
                return -1;
            return depthImagePixels[left].Depth;
        }

        private int GetLeftIndex(int i, int width, int length)
        {
            int row = i / width;
            int column = i % width;
            int above = (row - 1) * width + column;
            if (above < 0 || above >= length)
                return -1;
            return above;
        }

        private short GetDepthFromRightPixel(DepthImagePixel[] depthImagePixels, int i, int width)
        {
            int row = i / width;
            int column = i % width;
            int right = row * width + column + 1;
            if (right < 0 || right >= depthImagePixels.Length)
                return -1;
            return depthImagePixels[right].Depth;
        }
    }

    internal class GraphicalObject
    {
        public List<GraphicalPoint> GraphicalPoints { get; set; }

        public GraphicalObject()
        {
                GraphicalPoints = new List<GraphicalPoint>();
        }
    }

    internal struct GraphicalPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Depth { get; set; }
        public byte Color { get; set; }
    }
}