using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
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

        private const short depthDelta = 20;
        private Color[,] colorArray;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            processingLock = new object();
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
                this.sensor.DepthStream.Enable(DepthImageFormat.Resolution80x60Fps30);
                
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
        private object processingLock;

        /// <summary>
        /// Event handler for Kinect sensor's DepthFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            bool frameReady = false;
            lock (processingLock)
            {
                frameReady = !frameProcessing;
            }
            if (frameReady)
            {
                using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
                {
					if (depthFrame != null)
					{
						if (diffArray == null)
							diffArray = new short[depthFrame.Width, depthFrame.Height];
						depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
					    lock (processingLock)
					    {
                            frameProcessing = true;
					    }
						this.Dispatcher.BeginInvoke(DispatcherPriority.Background,
							(Action) (() => this.ProcessDepthData(depthFrame)));
					}
                }
            }
        }

        private void ProcessDepthData(DepthImageFrame frame)
        {
            System.Windows.Point pos = Mouse.GetPosition(Image);
            int posX = (int) pos.X;
            int posY = (int) pos.Y;
            if (posX < 0)
                posX = 0;
            if (posX >= frame.Width)
                posX = frame.Width - 1;
            if (posY < 0)
                posY = 0;
            if (posY >= frame.Height)
                posY = frame.Height - 1;
            colorArray = new Color[frame.Width, frame.Height];
            
            updateDataLabel(posX, posY);

            var minDepth = frame.MinDepth;
            var maxDepth = frame.MaxDepth;

            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    diffArray[i, j] = depthPixels[i * frame.Height + j].Depth;
                    colorArray[i,j] = Color.FromArgb(0, 0, 0, 0);
                }
            }

            int dx;
            int dy;

            //Calculate diffs
            //for (int i = 0; i < frame.Width - 1; i++)
            //{
            //    for (int j = 0; j < frame.Height - 1; j++)
            //    {
            //        dx = diffArray[i, j] - diffArray[i + 1, j];
            //        dy = diffArray[i, j] - diffArray[i, j + 1];

            //        diffArray[i, j] = (short)(dy);
            //    }
            //}

            //Convert the depth to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    // Write out blue byte
                    colorPixelIndex = 4 * (i * frame.Height + j);
                    this.colorPixels[colorPixelIndex] = 0;
                    this.colorPixels[colorPixelIndex + 1] = 0;
                    this.colorPixels[colorPixelIndex + 2] = 0;
                    //if (diffArray[i, j] == 0)
                    //{
                    //    this.colorPixels[colorPixelIndex] = 0;
                    //    this.colorPixels[colorPixelIndex + 1] = 255;
                    //    this.colorPixels[colorPixelIndex + 2] = 0;
                    //}
                    //else
                    //{

                    //    this.colorPixels[colorPixelIndex] = (byte)diffArray[i, j];

                    //    // Write out green byte
                    //    this.colorPixels[colorPixelIndex + 1] = (byte)(diffArray[i, j]);

                    //    //if (Math.Abs(diffArray[i, j]) >= depthDelta)
                    //    //{
                    //    //    // Write out red byte
                    //    //    this.colorPixels[colorPixelIndex++] = 255;
                    //    //}
                    //    //else
                    //    //{
                    //    //    // Write out red byte                        
                    //    //    this.colorPixels[colorPixelIndex++] = 0;
                    //    //}
                    //    this.colorPixels[colorPixelIndex + 2] = (byte)(diffArray[i, j]);
                    //}

                    // We're outputting BGR, the last byte in the 32 bits is unused so skip it
                    // If we were outputting BGRA, we would write alpha here.
                }
            }

            //bool[,] visited = new bool[frame.Width, frame.Height];
            //List<Point> visitedList = new List<Point>();
            //var q = new Queue<Point>();
            //q.Enqueue(new Point(posX, posY));
            ////PrintPixelDepth(diffArray);
            //while (q.Count > 0)
            //{
            //    var point = q.Dequeue();
                
            //    var pointsToAdd = GetAdjacentPoints(point);
            //    foreach (var pointToAdd in pointsToAdd)
            //    {
            //        if (!visited[pointToAdd.X, pointToAdd.Y])
            //        {
            //            if (Math.Abs(diffArray[pointToAdd.X, pointToAdd.Y] - diffArray[point.X, point.Y]) < depthDelta)
            //            {
            //                colorPixelIndex = 4 * (point.X * frame.Height + point.Y);
            //                colorPixels[colorPixelIndex] = 255;
            //                colorPixels[colorPixelIndex + 1] = 0;
            //                colorPixels[colorPixelIndex + 2] = 0;
            //                q.Enqueue(pointToAdd);
                            
            //            }
            //            else
            //            {
            //                colorPixelIndex = 4 * (point.X * frame.Height + point.Y);
            //                colorPixels[colorPixelIndex] = 0;
            //                colorPixels[colorPixelIndex + 1] = 0;
            //                colorPixels[colorPixelIndex + 2] = 255;
            //            }
            //            visited[pointToAdd.X, pointToAdd.Y] = true;
            //            visitedList.Add(pointToAdd);
            //        }
            //    }
            //}

            colorArray[posX, posY] = Color.FromArgb(0, 255, 0, 0);

            // Write the pixel data into our bitmap
            this.colorBitmap.WritePixels(
                new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight),
                this.colorPixels,
                this.colorBitmap.PixelWidth * sizeof(int),
                0);
            
            lock (processingLock)
            {
                frameProcessing = false;
            }
        }

        private void PrintPixelDepth(short[,] shorts)
        {
            Debug.WriteLine(" ");
            for (int i = 0; i < shorts.GetLength(0); i++)
            {
                for (int j = 0; j < shorts.GetLength(1); j++)
                {
                    Debug.Write(shorts[i,j] + " ");
                }
                Debug.WriteLine(" ");
            }
        }

        private List<Point> GetAdjacentPoints(Point position)
        {
            var list = new List<Point>();
            var pos1 = new Point(position.X + 1, position.Y);
            var pos2 = new Point(position.X - 1, position.Y);
            var pos3 = new Point(position.X, position.Y + 1);
            var pos4 = new Point(position.X, position.Y - 1);
            if (IsPositionValid(pos1))
                list.Add(pos1);
            if (IsPositionValid(pos2))
                list.Add(pos2);
            if (IsPositionValid(pos3))
                list.Add(pos3);
            if (IsPositionValid(pos4))
                list.Add(pos4);
            return list;

        }

        private bool IsPositionValid(Point p)
        {
            return p.X >= 0 && p.Y >= 0 && p.X < diffArray.GetLength(0) && p.Y < diffArray.GetLength(1);
        }

        /// <summary>
        /// Retrieves appropriate value from the frame and displays it in the 
        /// DataLabel. Arguments are position relative to the image control.
        /// </summary>
        private void updateDataLabel(int scrX, int scrY)
        {
            if (diffArray!=null &&
                scrX >= 0 && scrX < Image.Width &&
                scrY >= 0 && scrY < Image.Height)
            {
                int x = (int)((scrX / (double)Image.Width) * diffArray.GetLength(0));
                int y = (int)((scrY / (double)Image.Height) * diffArray.GetLength(1));
                dataLabel.Content = "arr["+x+","+y+"]= "+diffArray[x, y].ToString();
            }
        }

        private void Image_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point pos=e.GetPosition(Image);
            updateDataLabel((int)pos.X, (int)pos.Y);
        }
    }

    public struct Point
    {
        public int X 
        {
            get { return _x; }
            set { _x = value; }
        }
        public int Y 
        { 
            get { return _y; } 
            set { _y = value; } 
        }

        private int _x;
        private int _y;

        public Point(int x, int y)
        {
            _x = x;
            _y = y;
        }
    }
}