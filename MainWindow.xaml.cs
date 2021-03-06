﻿using System.Collections;
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

        /// <summary>
        /// Intermediate storage for the camera data
        /// </summary>
        private byte[] cameraPixels;
        object cameraPixelsLock = new object();

        short[,] diffArray;
        /// <summary>
        /// Intermediate storage for the depth data converted to color
        /// </summary>
        private byte[] colorPixels;

        private short depthDelta = 10;
        private Color[,] colorArray;
        private object depthLock = new object();
        private object processingLock = new object();
        private Color highlightColor;
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            highlightColor = Colors.Salmon;
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

                // Turn on camera
                this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);

                // Allocate space for camera pixels
                this.cameraPixels=new byte[this.sensor.ColorStream.FramePixelDataLength];

                // This is the bitmap we'll display on-screen
                this.colorBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);

                // Set the image we display to point to the bitmap where we'll put the image data
                this.Image.Source = this.colorBitmap;

                // Add an event handler to be called whenever there is new depth frame data
                this.sensor.DepthFrameReady += this.SensorDepthFrameReady;

                this.sensor.ColorFrameReady += this.SensorColorFrameReady;

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
        private object useCameraImageLock = new object();
        private bool useCameraImage;

        private void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    lock (cameraPixels)
                    {
                        colorFrame.CopyPixelDataTo(this.cameraPixels);
                    }
                }
            }
        }

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
            int kinectDepth;
            lock (depthLock)
            {
                kinectDepth = depthDelta;
            }
            bool useCameraImageLocal = false;
            lock (useCameraImageLock)
            {
                useCameraImageLocal = useCameraImage;
            }

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

            if (useCameraImageLocal)
            {
                lock (cameraPixels)
                {
                    BytesToColors(cameraPixels, colorArray);
                }
            }
            

            updateDataLabel(posX, posY);

            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    diffArray[i, j] = depthPixels[j * frame.Width + i].Depth;
                    //colorArray[i,j] = Color.FromArgb(0, 0, 0, 0);
                }
            }
            if (!useCameraImage)
            {
                for (int i = 0; i < frame.Width; i++)
                {
                    for (int j = 0; j < frame.Height; j++)
                    {
                        if (diffArray[i, j] == 0)
                        {
                            colorArray[i, j] = Color.FromArgb(0, 0, 255, 0);
                        }
                        else
                        {
                            byte color = (byte)diffArray[i, j];
                            colorArray[i, j] = Color.FromArgb(0, color, color, color);
                        }
                    }
                }
            }
            

            bool[,] visited = new bool[frame.Width, frame.Height];
            List<Point> visitedList = new List<Point>();
            var q = new Queue<Point>();
            q.Enqueue(new Point(posX, posY));
            while (q.Count > 0)
            {
                var point = q.Dequeue();

                var pointsToAdd = GetAdjacentPoints(point);
                foreach (var pointToAdd in pointsToAdd)
                {
                    if (!visited[pointToAdd.X, pointToAdd.Y])
                    {
                        if (diffArray[pointToAdd.X, pointToAdd.Y] != 0 && Math.Abs(diffArray[pointToAdd.X, pointToAdd.Y] - diffArray[point.X, point.Y]) < kinectDepth)
                        {
                            if (useCameraImage)
                            {
                                var color = colorArray[pointToAdd.X, pointToAdd.Y];
                                colorArray[pointToAdd.X, pointToAdd.Y] =
                                    Color.FromArgb(0,
                                        (byte) ((color.R + highlightColor.R)/2),
                                        (byte) ((color.G + highlightColor.G)/2),
                                        (byte) ((color.B + highlightColor.B)/2));
                            }
                            else
                            {
                                colorArray[pointToAdd.X, pointToAdd.Y] = Colors.Red;
                            }
                            q.Enqueue(pointToAdd);
                        }
                        else
                        {
                            colorArray[pointToAdd.X, pointToAdd.Y] = Colors.Purple;
                        }
                        visited[pointToAdd.X, pointToAdd.Y] = true;
                        visitedList.Add(pointToAdd);
                    }
                }
            }

            //Myszka
            colorArray[posX, posY] = Colors.Red;
            ColorsToBytes(colorArray, colorPixels);

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
        /// 

        private void ColorsToBytes(Color[,] colors, byte[] bytes)
        {
            if (colors.Length*4 != bytes.Length)
                throw new ArgumentException("Non-matching array sizes!");

            int byteCount=0;
            byte r, g, b;
            for (int y = 0; y < colors.GetLength(1); y++)
                for(int x=0;x<colors.GetLength(0);x++)           
                {
                    r=colors[x,y].R;
                    g=colors[x,y].G;
                    b=colors[x,y].B;

                    bytes[byteCount++] = b;
                    bytes[byteCount++] = g;
                    bytes[byteCount++] = r;
                    byteCount++;
                }
        }

        private void BytesToColors(byte[] bytes, Color[,] colors)
        {
            if (colors.Length * 4 != bytes.Length)
                throw new ArgumentException("Non-matching array sizes!");

            int byteCount = 0;
            byte r, g, b;
            for(int y=0; y<colors.GetLength(1);y++)
                for (int x = 0; x < colors.GetLength(0); x++)
                {
                    b = bytes[byteCount++];
                    g = bytes[byteCount++];
                    r = bytes[byteCount++];
                    byteCount++;
                    colors[x, y] = Color.FromArgb(0, r, g, b);
                }
        }

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

        private void DepthSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (depthLock)
            {
                depthDelta = (short)depthSlider.Value;
            }
            if(sliderValueLabel != null)
                sliderValueLabel.Content = (short)depthSlider.Value;
        }

        private void CameraCheckBox_OnChecked(object sender, RoutedEventArgs e)
        {
            lock (useCameraImageLock)
            {
                useCameraImage = true;
            }
        }

        private void CameraCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
        {
            lock (useCameraImageLock)
            {
                useCameraImage = false;
            }
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