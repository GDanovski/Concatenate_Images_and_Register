using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CellToolDK;
using System.IO;
using BitMiracle.LibTiff.Classic;
using System.Reflection;
using System.Diagnostics;
using System.ComponentModel;

namespace Concatenate_Images_and_Register
{
    public class Main
    {
        LoadingForm f = new LoadingForm();
        System.Timers.Timer t = new System.Timers.Timer(1000);

        //private Transmiter t;
        //private TifFileInfo fi;
        /*
        private void ApplyChanges()
        {
            t.ReloadImage();
        }
        
        private void CreateDialog()
        {

            Form form1 = new Form();
            form1.Text = "PlugIn";

            Button btn = new Button();
            btn.Text = "Start";
            form1.Controls.Add(btn);

            btn.Click += new EventHandler(delegate (object sender, EventArgs a) {
                ApplyChanges();
            });

            form1.Show();

        }
        */
        public void Input(TifFileInfo fi, Transmiter t)
        {
            //this.t = t;
            //this.fi = fi;
            string dir = "";
            if (fi != null) dir = fi.Dir;
            //CreateDialog();
            ProcessWorkDirectory(dir);
        }
        private void ProcessWorkDirectory(string dir)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "Add work directory:";

            if (dir != "" && dir.IndexOf("\\") > -1 &&
                File.Exists(dir) &&
                !Directory.Exists(dir))
                dir = dir.Substring(0, dir.LastIndexOf("\\"));

            if (Directory.Exists(dir))
                fbd.SelectedPath = dir;

            DialogResult result = fbd.ShowDialog();
            // OK button was pressed.
            if (result == DialogResult.OK)
            {
                dir = fbd.SelectedPath;
                string destination = dir + "\\Ready";
                int fileCounter = 0;

                string ijpath = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) 
                    + "\\CellToolPlugIns\\ijpath.txt");

                if (!File.Exists(ijpath))
                {
                    MessageBox.Show("ImageJ directory not set!");
                    return;
                }

                //find tif file names
                List<string> fileNames = ExtractFileList(dir);
                string[] fileMetadata = new string[fileNames.Count];

                if (fileNames.Count == 0) return;

                var bgw = new BackgroundWorker();
                bgw.WorkerReportsProgress = true;
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                //Add event for projection here
                bgw.DoWork += new DoWorkEventHandler(delegate (Object o, DoWorkEventArgs a)
                {
                    ((BackgroundWorker)o).ReportProgress(0);
                    string[] fileFullNames = new string[fileNames.Count];
                    //extract metadata
                    for (int i = 0; i < fileNames.Count; i++)
                    {
                        string str = ReadCellTool3_tags(dir + "\\" + fileNames[i]);

                        if (str != "")
                            fileMetadata[i] = str;

                        fileFullNames[i] = (dir + "\\" + fileNames[i]).Replace("\\", "\\\\");
                    }
                    ((BackgroundWorker)o).ReportProgress(1);
                    //Prepare ImageJ macro
                    string cmd = " -eval \"setBatchMode(true);a = newArray('";
                    cmd += string.Join("','", fileFullNames) + "');";

                    Assembly _assembly;
                    StreamReader _textStreamReader;
                    _assembly = Assembly.GetExecutingAssembly();

                    _textStreamReader = new StreamReader(_assembly.GetManifestResourceStream("Concatenate_Images_and_Register.IJM.txt"));

                    cmd += _textStreamReader.ReadToEnd();

                    //Stard cmd and cal for imageJ
                   
                        process.StartInfo.FileName = ijpath;
                        process.StartInfo.Arguments = cmd;
                        process.StartInfo.ErrorDialog = true;
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                        process.Start();
                        process.WaitForExit();
                    
                    //Loading metadata
                    ((BackgroundWorker)o).ReportProgress(2);

                    for(int i = 0; i < fileNames.Count; i++)
                        if(fileMetadata[i]!="")
                    {
                        AddTag(destination + "\\" + fileNames[i] + "_CompositeRegistred.tif",
                             fileMetadata[i]);
                    }
                    //finalizing
                    ((BackgroundWorker)o).ReportProgress(3);
                });

                bgw.ProgressChanged += new ProgressChangedEventHandler(delegate (Object o, ProgressChangedEventArgs a)
                {
                    switch (a.ProgressPercentage)
                    {
                        case 0:
                            //reading metadata
                            if (!Directory.Exists(destination))
                                Directory.CreateDirectory(destination);

                            DirectoryInfo directoryInfo = new DirectoryInfo(destination);

                            fileCounter = directoryInfo.GetFiles().Length;
                            
                            f.progressBar1.Maximum = fileNames.Count*2;
                            f.progressBar1.Value = 0;

                            f.ShowDialog();
                            break;
                        case 1:
                            //registration
                            f.statusLB.Text = "Processed images: 0";
                           
                            t.Elapsed += new System.Timers.ElapsedEventHandler(delegate (Object source, System.Timers.ElapsedEventArgs e) 
                            {
                                directoryInfo = new DirectoryInfo(destination);
                                int files = directoryInfo.GetFiles().Length - fileCounter;
                                f.progressBar1.Value = files;
                                f.statusLB.Text = "Processed images: " + ((int)(files/2)).ToString();
                            });

                            t.AutoReset = true;
                            t.Enabled = true;
                            t.Start();
                            break;
                        case 2:
                            //loading metadata
                            t.Stop();
                            f.statusLB.Text = "Loading metadata...";
                            break;
                        case 3:
                            //exit
                            t.Dispose();
                            f.Close();
                            break;
                    }
                });
                f.FormClosing += new FormClosingEventHandler(delegate (object o, FormClosingEventArgs e)
                {
                    try
                    {
                        t.Stop();
                        t.Dispose();
                        process.Kill();
                    }
                    catch { }
                });
                //start bgw
                bgw.RunWorkerAsync();
            }
        }
        #region Custom Tag
        private const TiffTag TIFFTAG_CellTool_METADATA = (TiffTag)40005;

        private static Tiff.TiffExtendProc m_parentExtender;

        private static void TagExtender(Tiff tif)
        {
            TiffFieldInfo[] tiffFieldInfo =
            {
                new TiffFieldInfo(TIFFTAG_CellTool_METADATA, -1, -1, TiffType.ASCII,
                    FieldBit.Custom, true, false, "CellTool_Metadata"),
            };

            tif.MergeFieldInfo(tiffFieldInfo, tiffFieldInfo.Length);

            if (m_parentExtender != null)
                m_parentExtender(tif);
        }

        private static void AddTag(string dir, string value)
        {
            // Register the extender callback 
            m_parentExtender = Tiff.SetTagExtender(TagExtender);

            using (Tiff image = Tiff.Open(dir, "a"))
            {
                image.SetDirectory((short)(image.NumberOfDirectories() - 1));
                // we should rewind to first directory (first image) because of append mode

                // set the custom tag  
                image.SetField(TIFFTAG_CellTool_METADATA, value);

                // rewrites directory saving new tag
                image.CheckpointDirectory();
            }

            // restore previous tag extender
            Tiff.SetTagExtender(m_parentExtender);
        }
        #endregion Custom Tag
        private List<string> ExtractFileList(string dir)
        {
            List<string> l = new List<string>();

            if (!Directory.Exists(dir)) { return l; }

            DirectoryInfo directoryInfo = new DirectoryInfo(dir);

            foreach (var file in directoryInfo.GetFiles())
                if (file.FullName.EndsWith(".tif"))
                {
                    string name = file.Name.Substring(0,file.Name.IndexOf("_"));

                    if (l.IndexOf(name) == -1)
                        l.Add(name);
                }

            return l;
        }
        private string ReadCellTool3_tags(string path)
        {
            string[] vals = null;
            List<double> timeSteps = new List<double>();
            int imageCount = 0;
            int sizeT = 0;
            
            Tiff image = Tiff.Open(path + "_1.tif", "r");
            {
                image.SetDirectory((short)(image.NumberOfDirectories() - 1));
                // read auto-registered tag 50341
                FieldValue[] value = image.GetField((TiffTag)40005);//CellTool3 tif tag
                
                if (value != null)
                {
                    vals = value[1].ToString().Split(new string[] { ";\n" }, StringSplitOptions.None);

                    double[] tempRes = extractTimeSteps(vals);

                    timeSteps.Add(tempRes[0]);
                    timeSteps.Add(tempRes[1]);
                    imageCount += image.NumberOfDirectories();
                    sizeT += (int)tempRes[0];
                }
                else
                {
                    image.Close();
                    return "";
                }

                image.Close();
            }
            //read all files
            int index = 2;

            while(File.Exists(path + "_" + index.ToString() + ".tif"))
            {
                image = Tiff.Open(path + "_" + index.ToString() + ".tif", "r");
                {
                    image.SetDirectory((short)(image.NumberOfDirectories() - 1));
                    // read auto-registered tag 50341
                    FieldValue[] value = image.GetField((TiffTag)40005);//CellTool3 tif tag
                    if (value != null)
                    {
                        double[] tempRes = extractTimeSteps(value[1].ToString().Split(new string[] { ";\n" }, StringSplitOptions.None));
                        timeSteps.Add(tempRes[0]);
                        timeSteps.Add(tempRes[1]);
                        imageCount += image.NumberOfDirectories();
                        sizeT += (int)tempRes[0];
                    }
                    image.Close();
                }

                index++;
            }
            //prepare tag
            List<string> newVals = vals.ToList();
            for(int i = 0; i < newVals.Count; i++)
            {
                if (newVals[i].StartsWith("imageCount->"))
                    newVals[i] = "imageCount->" + imageCount.ToString();
                else if (newVals[i].StartsWith("sizeT->"))
                    newVals[i] = "sizeT->" + sizeT.ToString();
                else if (newVals[i].StartsWith("TimeSteps->"))
                    newVals[i] = "TimeSteps->" + TagValueToString(timeSteps);
            }
            
            return string.Join(";\n", newVals);
        }
        private static string TagValueToString(List<double> dList)
        {
            string val = "";
            foreach (double d in dList)
                val += d.ToString() + "\t";
            return val;
        }
        private double[] extractTimeSteps(string[] BigVals)
        {
            string[] vals = null;
            double[] res = new double[2];

            foreach (string val in BigVals)
            {
                vals = val.Split(new string[] { "->" }, StringSplitOptions.None);
                switch (vals[0])
                {
                    case ("sizeT"):
                        res[0] = double.Parse(vals[1]);
                        break;
                    case "TimeSteps":
                        res[1] = StringToTagValue(vals[1])[1];
                        break;
                }
            }

            return res;
        }
        private List<double> StringToTagValue(string val)
        {
            List<double> res = new List<double>();
            foreach (string i in val.Split(new string[] { "\t" }, StringSplitOptions.None))
                if (i != "")
                    res.Add(double.Parse(i));

            return res;
        }
    }
}
