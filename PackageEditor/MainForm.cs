using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Collections;
using System.Diagnostics;
using System.Xml;
using System.Resources;
using System.Reflection;
using Microsoft.Win32;
using VirtPackageAPI;
using PackageEditor.FilesEditing;
using Delay;
using Cameyo.OpenSrc.Common;

namespace PackageEditor
{
    public partial class MainForm : Form
    {
        private VirtPackage virtPackage;
        private FileSystemEditor fsEditor;
        private RegistryEditor regEditor;
        private bool regLoaded;
        private Thread regLoadThread;
        private MRU mru;
        public bool dirty;
        private bool dragging;
        private Control[] Editors;

        // creation of delegate for PackageOpen
        private delegate bool DelegatePackageOpen(String path);
        DelegatePackageOpen Del_Open;

        public MainForm(string packageExeFile, bool notifyPackageBuilt)
        {
            InitializeComponent();
            dragging = false;

            panelWelcome.Parent = this;
            panelWelcome.BringToFront();
            panelWelcome.Dock = DockStyle.None;
            panelWelcome.Dock = DockStyle.Fill;
            tabControl.TabPages.Remove(tabWelcome);

            // delegate for PackageOpen init
            Del_Open = new DelegatePackageOpen(this.PackageOpen);

            tabControl.Visible = false;
            panelWelcome.Visible = !tabControl.Visible;
            regLoaded = false;
            dirty = false;
            virtPackage = new VirtPackage();
            mru = new MRU("Software\\Cameyo\\Packager\\MRU");

            fsEditor = new FileSystemEditor(virtPackage, fsFolderTree, fsFilesList,
                fsFolderInfoFullName, fsFolderInfoIsolationCombo, fsAddBtn, fsRemoveBtn, fsAddEmptyDirBtn, fsSaveFileAsBtn, fsAddDirBtn);
            regEditor = new RegistryEditor(virtPackage, regFolderTree, regFilesList,
                regFolderInfoFullName, regFolderInfoIsolationCombo, regRemoveBtn, regEditBtn);

            regFilesList.DoubleClickActivation = true;
            Editors = new Control[] { tbFile, tbValue, tbType, tbSize };

            EnableDisablePackageControls(false);       // No package opened yet; disable Save menu etc
            if (packageExeFile != "" && !packageExeFile.Equals("/OPEN", StringComparison.InvariantCultureIgnoreCase))
            {
                if (PackageOpen(packageExeFile) && notifyPackageBuilt)
                {
                    PackageBuiltNotify packageBuiltNotify = new PackageBuiltNotify();
                    String friendlyPath = packageExeFile;
                    int pos = friendlyPath.ToUpper().IndexOf("\\DOCUMENTS\\");
                    if (pos == -1) pos = friendlyPath.ToUpper().IndexOf("\\MY DOCUMENTS\\");
                    if (pos != -1) friendlyPath = friendlyPath.Substring(pos + 1);

                    System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
                    packageBuiltNotify.Do(PackageEditor.Messages.Messages.packageBuiltOk, packageExeFile, friendlyPath, "PackageBuiltNotify");
                }
            }

#if DropBox
            if (!System.IO.File.Exists(Application.StartupPath + "\\AppLimit.CloudComputing.oAuth.dll")
                || !System.IO.File.Exists(Application.StartupPath + "\\AppLimit.CloudComputing.SharpBox.dll")
                || !System.IO.File.Exists(Application.StartupPath + "\\Newtonsoft.Json.Net20.dll"))
            {
                dropboxLabel.Hide();
                dropboxButton.Hide();
                resetCredLink.Hide();
                MessageBox.Show("This version is compiled with DropBox funtionality, but one or more of the dlls needed are missing:\nAppLimit.CloudComputing.oAuth.dll\nAppLimit.CloudComputing.SharpBox.dll\nNewtonsoft.Json.Net20.dll\n\nThe button will be hidden", "Missing DLL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
#else
            dropboxLabel.Hide();
            dropboxButton.Hide();
            //resetCredLink.Hide();
#endif

            // Culture
            langToolStripMenuItem.DropDownItems.Clear();
            ToolStripMenuItem item;
            foreach (Cameyo.OpenSrc.Common.LangItem lang in Cameyo.OpenSrc.Common.LangItem.SupportedLangs())
            {
                item = new ToolStripMenuItem(lang.DisplayName) { Tag = lang.Culture };
                item.Click += langMenuItem_Click;
                langToolStripMenuItem.DropDownItems.Add(item);
            }
            CurLanguageMenuItem().Checked = true;
        }

        private void ThreadedRegLoad()
        {
            regEditor.OnPackageOpenBeforeUI();
            regLoaded = true;
            regLoadThread = null;
        }

        private void ThreadedRegLoadStop()
        {
            regEditor.threadedRegLoadStop();
            if (regLoadThread != null)
            {
                regLoadThread.Join();
                regLoadThread = null;
            }
        }

        private void regProgressTimer_Tick(object sender, EventArgs e)
        {
            if (regLoaded)
            {
                regEditor.OnPackageOpenUI();
                regProgressBar.Visible = false;
                regToolStrip.Visible = true;
                regSplitContainer.Visible = true;
                regProgressTimer.Enabled = false;
                return;
            }
            regProgressBar.Value += 5;
            if (regProgressBar.Value >= 100)
                regProgressBar.Value = 0;
        }

        private void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl.SelectedTab == tabGeneral)
                this.OnTabActivate();
            else if (tabControl.SelectedTab == tabFileSystem)
                fsEditor.OnTabActivate();
            else if (tabControl.SelectedTab == tabRegistry)
                regEditor.OnTabActivate();
        }

        private void EnableDisablePackageControls(bool enable)
        {
            tabControl.Visible = enable;
            panelWelcome.Visible = !enable;
            saveToolStripMenuItem.Enabled = enable;
            saveasToolStripMenuItem.Enabled = enable;
            closeToolStripMenuItem.Enabled = enable;
        }

        private bool PackageOpen(String packageExeFile)
        {
            VirtPackage.APIRET apiRet;
            bool ret = PackageOpen(packageExeFile, true, out apiRet);
            return (ret);
        }

        private bool PackageOpen(String packageExeFile, bool displayWaitMsg, out VirtPackage.APIRET apiRet)
        {
            bool ret;
            apiRet = 0;
            if (virtPackage.opened && !PackageClose())      // User doesn't want to discard changes
                return false;
            if (displayWaitMsg)
            {
                System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
                PleaseWait.PleaseWaitBegin(PackageEditor.Messages.Messages.openingPackage, PackageEditor.Messages.Messages.opening + " " + System.IO.Path.GetFileName(packageExeFile) + "...", packageExeFile);
            }

            ret = virtPackage.Open(packageExeFile, out apiRet);

            // OLD_VERSION conversion
            if (!ret && apiRet == VirtPackage.APIRET.OLD_VERSION)
            {
                if (displayWaitMsg)
                    PleaseWait.PleaseWaitEnd();     // Otherwise it'll hide our below MessageBox
                if (MessageBox.Show("This package was built with an older version and needs to be converted.\nConvert now?",
                    "Conversion required", MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes)
                {
                    int exitCode = 0;
                    if (ExecProg(PackagerExe(),"-Quiet -ConvertOldPkg \"" + packageExeFile + "\"", true, ref exitCode) && exitCode == 0)
                    {
                        string newPkgFile = packageExeFile, oldPkgFile = packageExeFile;
                        int pos = newPkgFile.LastIndexOf('.');
                        newPkgFile = newPkgFile.Insert(pos, ".new");
                        oldPkgFile = oldPkgFile.Insert(pos, ".old");
                        bool trouble = false;
                        try { File.Delete(oldPkgFile); } catch { }
                        try 
                        { 
                            File.Move(packageExeFile, oldPkgFile);
                        } 
                        catch 
                        { 
                            MessageBox.Show("Package could not be renamed to:\n" + oldPkgFile);
                            trouble = true;
                        }
                        try
                        {
                            File.Move(newPkgFile, packageExeFile);
                        }
                        catch 
                        { 
                            MessageBox.Show("New package could not be renamed to:\n" + packageExeFile);
                            trouble = true;
                        }
                        if (!trouble)
                        {
                            ret = virtPackage.Open(packageExeFile, out apiRet);
                            MessageBox.Show("Package was successfully converted. Your old package was saved in:\n" + oldPkgFile);
                        }
                    }
                    else
                        MessageBox.Show("Error converting package! " + exitCode);
                }
            }

            if (ret)
            {
                regLoaded = false;
                dirty = false;
                this.OnPackageOpen();
                fsEditor.OnPackageOpen();

                // regEditor (threaded)
                regProgressBar.Visible = true;
                regToolStrip.Visible = false;
                regSplitContainer.Visible = false;
                regProgressTimer.Enabled = true;

                ThreadedRegLoadStop();
                regLoadThread = new Thread(ThreadedRegLoad);
                regLoadThread.Start();

                tabControl.SelectedIndex = 0;
                EnableDisablePackageControls(true);
                mru.AddFile(packageExeFile);

                ret = true;
            }
            else
                ret = false;

            if (displayWaitMsg)
                PleaseWait.PleaseWaitEnd();

            return ret;
        }

        private bool PackageClose()
        {
            return PackageClose(true);
        }

        private bool PackageClose(bool disableControls)
        {
            if (virtPackage.opened == false)
                return true;
            if (this.dirty || fsEditor.dirty || regEditor.dirty)
            {
                System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
                if (MessageBox.Show(PackageEditor.Messages.Messages.saveChanges, PackageEditor.Messages.Messages.confirm, MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    saveToolStripMenuItem_Click(this, null);
                    if (this.dirty || fsEditor.dirty || regEditor.dirty)    // Still not saved
                        return false;
                }
            }

            // If regLoadThread is working, wait for it to finish
            ThreadedRegLoadStop();

            this.OnPackageClose();
            fsEditor.OnPackageClose();
            regEditor.OnPackageClose();
            virtPackage.Close();
            if (disableControls)
                EnableDisablePackageControls(false);
            return true;
        }

        private bool PackageCanSave(out String message)
        {
            message = "";
            if (String.IsNullOrEmpty(propertyAppID.Text))
                message += "- AppID is a required field to save a package.\r\n";
            if (String.IsNullOrEmpty(virtPackage.GetProperty("AutoLaunch")))
                message += "- The package does not have any program(s) selected to launch.\r\nPlease select a program to launch on the tab:General > Panel:Basics > Item:Startup.";

            return message == "";
        }

        private bool PackageSave(String fileName)
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));

            String CantSaveBecause = "";
            if (!PackageCanSave(out CantSaveBecause))
            {
                MessageBox.Show(this, CantSaveBecause, PackageEditor.Messages.Messages.cannotSave, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int ret = 0;
            VirtPackage.APIRET apiRet = 0;
            PleaseWait.PleaseWaitBegin(PackageEditor.Messages.Messages.savingPackage, 
                PackageEditor.Messages.Messages.saving + " " + System.IO.Path.GetFileName(fileName) + "...", virtPackage.openedFile);
            {
                ret = ret == 0 && !this.OnPackageSave() ? 1 : ret;
                ret = ret == 0 && !fsEditor.OnPackageSave() ? 2 : ret;
                ret = ret == 0 && !regEditor.OnPackageSave() ? 3 : ret;
                ret = ret == 0 && !virtPackage.SaveEx(fileName, out apiRet) ? 4 : ret;
            }
            PleaseWait.PleaseWaitEnd();

            if (ret == 0)
            {
                this.dirty = false;
                fsEditor.dirty = false;
                regEditor.dirty = false;
                return true;
            }
            else
            {
                MessageBox.Show(PackageEditor.Messages.Messages.cannotSave + " ApiRet:" + apiRet + " (step " + ret + ")");
                return false;
            }
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            //openFileDialog.InitialDirectory = System.Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "\\Cameyo apps";
            openFileDialog.Multiselect = false;
            openFileDialog.Filter = "Virtual app (*.cameyo.exe;*.virtual.exe)|*.cameyo.exe;*.virtual.exe|All files (*.*)|*.*";
            //openFileDialog.DefaultExt = "virtual.exe";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                VirtPackage.APIRET apiRet;
                if (!PackageOpen(openFileDialog.FileName, true, out apiRet))
                {
                    MessageBox.Show(String.Format("Failed to open package. API error: {0}", apiRet));
                }
            }
        }

        private void saveasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            string packageEditor = resources.GetString("$this.Text");

            String message;
            if (!PackageCanSave(out message))
            {
                MessageBox.Show(this, message, PackageEditor.Messages.Messages.cannotSave, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            if (!String.IsNullOrEmpty(virtPackage.openedFile))
            {
                saveFileDialog.InitialDirectory = Path.GetDirectoryName(virtPackage.openedFile);
                saveFileDialog.FileName = Path.GetFileName(virtPackage.openedFile);
            }
            else
            {
                saveFileDialog.FileName = "New app.cameyo.exe";
            }
            saveFileDialog.AddExtension = true;
            saveFileDialog.Filter = "Virtual app (*.cameyo.exe;*.virtual.exe)|*.cameyo.exe;*.virtual.exe";
            saveFileDialog.DefaultExt = "cameyo.exe";

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                if (PackageSave(saveFileDialog.FileName))
                {
                    virtPackage.openedFile = saveFileDialog.FileName;
                    this.Text = packageEditor + " - " + saveFileDialog.FileName;
                    MessageBox.Show(PackageEditor.Messages.Messages.packageSaved);
                }
            }
        }

        private void exportAsZeroInstallerXmlToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO:piba,ZeroInstaller, exportAsZeroInstallerXml
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.AddExtension = true;
            saveFileDialog.Filter = "ZeroInstaller configuration file (*.xml)|*.xml";
            saveFileDialog.DefaultExt = "xml";
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                XmlTextWriter xmlOut = new XmlTextWriter(saveFileDialog.FileName, Encoding.Default);
                xmlOut.Formatting = Formatting.Indented;
                xmlOut.WriteStartDocument();
                xmlOut.WriteStartElement("ZeroInstallerXml");

                xmlOut.WriteStartElement("Properties");
                xmlOut.WriteStartElement("Property");
                xmlOut.WriteAttributeString("AppID", "TestApp");
                xmlOut.WriteEndElement();
                xmlOut.WriteStartElement("Property");
                xmlOut.WriteAttributeString("Version", "1.0");
                xmlOut.WriteEndElement();
                xmlOut.WriteStartElement("Property");
                xmlOut.WriteAttributeString("IconFile", "Icon.exe");
                xmlOut.WriteEndElement();
                xmlOut.WriteStartElement("Property");
                xmlOut.WriteAttributeString("StopInheritance", "");
                xmlOut.WriteEndElement();
                xmlOut.WriteStartElement("Property");
                xmlOut.WriteAttributeString("BuildOutput", "[AppID].exe");
                xmlOut.WriteEndElement();
                xmlOut.WriteEndElement();

                xmlOut.WriteStartElement("FileSystem");
                xmlOut.WriteEndElement();

                xmlOut.WriteStartElement("Registry");
                xmlOut.WriteEndElement();

                xmlOut.WriteStartElement("Sandbox");
                xmlOut.WriteStartElement("FileSystem");
                xmlOut.WriteAttributeString("access", "Full");
                xmlOut.WriteEndElement();

                xmlOut.WriteStartElement("Registry");
                xmlOut.WriteAttributeString("access", "Full");
                xmlOut.WriteEndElement();
                xmlOut.WriteEndElement();

                xmlOut.WriteEndElement();
                xmlOut.WriteEndDocument();
                xmlOut.Flush();
                xmlOut.Close();

                xmlOut.Close();
            }
        }

        private void DisplayAutoLaunch()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            if (virtPackage.GetProperty("AutoLaunch").Contains(";"))
            {
                String[] autoLaunches = virtPackage.GetProperty("AutoLaunch").Split(';');
                propertyAutoLaunch.Text = "";
                propertyAutoLaunch.AutoEllipsis = true;
                for (int i = 0; i < autoLaunches.Length; i++)
                {
                    String[] items = autoLaunches[i].Split('>');
                    if (items.Length < 3) continue;     // No Name
                    if (propertyAutoLaunch.Text != "")
                        propertyAutoLaunch.Text += ", ";
                    propertyAutoLaunch.Text += VirtPackage.FriendlyShortcutName(items[2]);
                }
                propertyAutoLaunch.Text = PackageEditor.Messages.Messages.displayMenu + ": " + propertyAutoLaunch.Text;
            }
            else
            {
                String[] items = virtPackage.GetProperty("AutoLaunch").Split('>');
                if (items.Length >= 3)
                    propertyAutoLaunch.Text = items[0] + " (" + VirtPackage.FriendlyShortcutName(items[2]) + ")";
                else
                    propertyAutoLaunch.Text = items[0];
            }
        }

        private void OnPackageOpen()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            string packageEditor = resources.GetString("$this.Text");
            String command;

            // AppID
            propertyAppID.Text = virtPackage.GetProperty("AppID");
            //propertyAppID.TextChanged += PropertyChange;

            // FriendlyName
            propertyFriendlyName.Text = virtPackage.GetProperty("FriendlyName");
            //propertyFriendlyName.TextChanged += PropertyChange;

            // AutoLaunch
            //FillAutoLaunchCombo(propertyAutoLaunch);
            DisplayAutoLaunch();

            // BaseDirName
            DisplayBaseDirName();

            // VirtMode
            if (virtPackage.GetProperty("VirtMode").Equals("RAM", StringComparison.InvariantCultureIgnoreCase))
                propertyVirtModeRam.Checked = true;
            else
                propertyVirtModeDisk.Checked = true;

            // Isolation
            int isolationType = virtPackage.GetIsolationMode();
            propertyIsolationDataMode.Checked = (isolationType == VirtPackage.ISOLATIONMODE_DATA);
            propertyIsolationIsolated.Checked = (isolationType == VirtPackage.ISOLATIONMODE_ISOLATED);
            propertyIsolationMerge.Checked = (isolationType == VirtPackage.ISOLATIONMODE_FULL_ACCESS);
            if (propertyIsolationDataMode.Checked)
                virtPackage.SetProperty("DataMode", "TRUE");   // Important to be able to switch to Isolated mode (to unisolate %Personal% etc)

            // Icon
            if (!String.IsNullOrEmpty(virtPackage.openedFile))
            {
                Icon icon = Win32Function.getIconFromFile(virtPackage.openedFile);
                if (icon != null)
                {
                    propertyIcon.Image = icon.ToBitmap();
                }
            }

            // StopInheritance
            propertyStopInheritance.Text = virtPackage.GetProperty("StopInheritance");

            // Integrate
            command = GetIntegrateStartCommand();
            if (command == "")
                rdbIntegrateNone.Checked = true;
            else
            {
                if (command.EndsWith("-Integrate:once"))
                    rdbIntegrateStandard.Checked = true;
                else if (command.EndsWith("-Vintegrate"))
                    rdbIntegrateVirtual.Checked = true;
            }

            // CleanupOnExit
            command = GetCleanupStopCommand();
            if (command == "")
                rdbCleanNone.Checked = true;
            else
            {
                chkCleanAsk.Checked = command.Contains("-Confirm");
                chkCleanDoneDialog.Checked = !command.Contains("-Quiet");
                if (command.EndsWith("-Remove:Reg"))
                    rdbCleanRegOnly.Checked = true;
                else
                    rdbCleanAll.Checked = true;
            }


            // Expiration
            String expiration = virtPackage.GetProperty("Expiration");
            propertyExpiration.Checked = !String.IsNullOrEmpty(expiration);
            propertyExpirationDatePicker.Value = DateTime.Now;
            if (propertyExpiration.Checked)
            {
                String[] expirationItems = expiration.Split('/');
                if (expirationItems.Length == 3)
                {
                    int day = Convert.ToInt32(expirationItems[0]);
                    int month = Convert.ToInt32(expirationItems[1]);
                    int year = Convert.ToInt32(expirationItems[2]);
                    DateTime dt = new DateTime(year, month, day);
                    propertyExpirationDatePicker.Value = dt;
                }
            }

            if (!String.IsNullOrEmpty(virtPackage.openedFile))
                this.Text = packageEditor + " - " + virtPackage.openedFile;
            else
                this.Text = packageEditor;
            dirty = false;
        }

        private void RemoveIfStartswith(ref String str, String value)
        {
            String newStr = str.TrimStart(' ');
            if (newStr.StartsWith(value))
                str = newStr.Remove(0, value.Length);
        }

        private string GetIntegrateStartCommand()
        {
            String[] OnStartUnvirtualized = virtPackage.GetProperty("OnStartUnvirtualized").Split(';');
            foreach (String command in OnStartUnvirtualized)
            {
                if (command.StartsWith("%MyExe%" + '>') && (command.EndsWith("-Integrate:once") || command.EndsWith("-Vintegrate")))
                {
                    String checkNoRemains = command;
                    RemoveIfStartswith(ref checkNoRemains, "%MyExe%");
                    RemoveIfStartswith(ref checkNoRemains, ">");
                    RemoveIfStartswith(ref checkNoRemains, "-Quiet");

                    RemoveIfStartswith(ref checkNoRemains, "-Integrate:once");
                    RemoveIfStartswith(ref checkNoRemains, "-Vintegrate");
                    if (checkNoRemains == "")
                        return command;
                }
            }
            return "";
        }

        private string GetCleanupStopCommand()
        {
            String[] OnStopUnvirtualized = virtPackage.GetProperty("OnStopUnvirtualized").Split(';');
            foreach (String command in OnStopUnvirtualized)
            {
                if (command.StartsWith("%MyExe%" + '>') && (command.EndsWith("-Remove") || command.EndsWith("-Remove:Reg")))
                {
                    String checkNoRemains = command;
                    RemoveIfStartswith(ref checkNoRemains, "%MyExe%");
                    RemoveIfStartswith(ref checkNoRemains, ">");
                    RemoveIfStartswith(ref checkNoRemains, "-Confirm");
                    RemoveIfStartswith(ref checkNoRemains, "-Quiet");

                    RemoveIfStartswith(ref checkNoRemains, "-Remove:Reg");
                    RemoveIfStartswith(ref checkNoRemains, "-Remove");
                    if (checkNoRemains == "")
                        return command;
                }
            }
            return "";
        }

        public void OnTabActivate()
        {
            //no needed anymore: DisplayIsolation();
        }

        public bool OnPackageSave()
        {
            bool Ret = true;
            String str, oldCommand, newCommand;

            // AppID + AutoLaunch
            Ret &= virtPackage.SetProperty("AppID", propertyAppID.Text);
            Ret &= virtPackage.SetProperty("FriendlyName", propertyFriendlyName.Text);
            Ret &= virtPackage.SetProperty("StopInheritance", propertyStopInheritance.Text);
            if (propertyExpiration.Checked)
                Ret &= virtPackage.SetProperty("Expiration", propertyExpirationDatePicker.Value.ToString("dd/MM/yyyy"));
            else
                Ret &= virtPackage.SetProperty("Expiration", "");
            if (propertyVirtModeRam.Checked)
                Ret &= virtPackage.SetProperty("VirtMode", "RAM");
            else
                Ret &= virtPackage.SetProperty("VirtMode", "");

            // propertyIntegrate, propertyVintegrate
            str = virtPackage.GetProperty("OnStartUnvirtualized");
            oldCommand = GetIntegrateStartCommand();
            newCommand = "";
            if (!rdbIntegrateNone.Checked)
            {
                newCommand += ' ' + "-Quiet";
                if (rdbIntegrateStandard.Checked)
                    newCommand += ' ' + "-Integrate:once";
                if (rdbIntegrateVirtual.Checked)
                    newCommand += ' ' + "-Vintegrate";

                newCommand = "%MyExe%" + '>' + newCommand.Trim();
            }
            if (oldCommand == "")
                str += ";" + newCommand;
            else
            {
                str = ";" + str + ";";
                str = str.Replace(";" + oldCommand + ";", ";" + newCommand + ";");
                str = str.Replace(";;", ";");
                str = str.Trim(';');
            }
            Ret &= virtPackage.SetProperty("OnStartUnvirtualized", str);

            // propertyCleanupOnExit
            str = virtPackage.GetProperty("OnStopUnvirtualized");
            oldCommand = GetCleanupStopCommand();
            newCommand = "";
            if (!rdbCleanNone.Checked)
            {
                if (chkCleanAsk.Checked)
                    newCommand += ' ' + "-Confirm";
                if (!chkCleanDoneDialog.Checked)
                    newCommand += ' ' + "-Quiet";

                if (rdbCleanRegOnly.Checked)
                    newCommand += ' ' + "-Remove:Reg";
                if (rdbCleanAll.Checked)
                    newCommand += ' ' + "-Remove";

                newCommand = "%MyExe%" + '>' + newCommand.Trim();
            }
            if (oldCommand == "")
                str += ";" + newCommand;
            else
            {
                str = ";" + str + ";";
                str = str.Replace(";" + oldCommand + ";", ";" + newCommand + ";");
                str = str.Replace(";;", ";");
                str = str.Trim(';');
            }
            Ret &= virtPackage.SetProperty("OnStopUnvirtualized", str);
            /*
            if (propertyCleanupOnExit.Checked)
            {
                if (!str.Contains(CleanupOnExitCmd))
                {
                    if (str != "")
                        str += ";";
                    str += CleanupOnExitCmd;
                    Ret &= virtPackage.SetProperty("OnStopUnvirtualized", str);
                }
            }
            else
            {
                if (str.Contains(CleanupOnExitCmd))
                {
                    str = str.Replace(CleanupOnExitCmd, "");
                    str = str.Replace(";;", ";");
                    str = str.Trim(';');
                    Ret &= virtPackage.SetProperty("OnStopUnvirtualized", str);
                }
            }*/

            // AutoLaunch (and SaveAutoLaunchCmd + SaveAutoLaunchMenu) already set by AutoLaunchForm

            // BaseDirName already set by SetProperty

            // Isolation. Note: it is allowed to have no checkbox selected at all.
            virtPackage.SetIsolationMode(
                propertyIsolationIsolated.Checked ? VirtPackage.ISOLATIONMODE_ISOLATED :
                propertyIsolationMerge.Checked ? VirtPackage.ISOLATIONMODE_FULL_ACCESS :
                propertyIsolationDataMode.Checked ? VirtPackage.ISOLATIONMODE_DATA :
                VirtPackage.ISOLATIONMODE_CUSTOM);

            // SetIcon already set when icon button is pressed

            return (Ret);
        }

        private void OnPackageClose()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            string packageEditor = resources.GetString("$this.Text");

            propertyAppID.Text = "";
            propertyFriendlyName.Text = "";
            propertyAutoLaunch.Text = "";
            propertyIcon.Image = null;
            propertyStopInheritance.Text = "";
            //propertyCleanupOnExit.Checked = false;
            this.Text = packageEditor;
        }

        private bool DeleteFile(String FileName)
        {
            bool ret = true;
            try
            {
                System.IO.File.Delete(FileName);
            }
            catch
            {
                ret = false;
            }
            return ret;
        }

        private bool MoveFile(String srcFileName, String destFileName)
        {
            bool ret = true;
            try
            {
                System.IO.File.Move(srcFileName, destFileName);
            }
            catch
            {
                ret = false;
            }
            return ret;
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(virtPackage.openedFile))
            {
                // Its a new package.. so save as to get a filename.
                saveasToolStripMenuItem_Click(sender, e);
                return;
            }
            String tmpFileName = virtPackage.openedFile + ".new";
            DeleteFile(tmpFileName);
            //DeleteFile(virtPackage.openedFile + ".bak");
            if (PackageSave(tmpFileName))
            {
                // Release (close) original file, and delete it (otherwise it won't be erasable)
                String packageExeFile = virtPackage.openedFile;

                ThreadedRegLoadStop();
                PackageClose(false);
                //virtPackage.Close();

                DeleteFile(packageExeFile);
                bool ok;
                ok = MoveFile(tmpFileName, packageExeFile);
                VirtPackage.APIRET apiRet;
                if (!PackageOpen(packageExeFile, false, out apiRet))
                    MessageBox.Show(apiRet.ToString());
                String property = virtPackage.GetProperty("AutoLaunch");
                //virtPackage.Open(packageExeFile);
                if (ok)
                    MessageBox.Show("Package saved.");
                else
                    MessageBox.Show("Cannot rename: " + tmpFileName + " to: " + packageExeFile);
            }
            else
            {
                // Save failed. Delete .new file.
                System.IO.File.Delete(virtPackage.openedFile + ".new");
            }
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PackageClose();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PackageClose())
                this.Close();
        }

        private void DisplayBaseDirName()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DataStorageForm));
            string propertyLocalStorageDefault = resources.GetString("propertyLocalStorageDefault.Text");
            string propertyLocalStorageExeDir = resources.GetString("propertyLocalStorageExeDir.Text");
            string propertyLocalStorageCustom = resources.GetString("propertyLocalStorageCustom.Text");

            String baseDirName = virtPackage.GetProperty("BaseDirName");
            if (baseDirName == "")
                propertyDataStorage.Text = propertyLocalStorageDefault;   // Use hard disk or USB drive (wherever application is launched from)
            else if (baseDirName.Equals("%ExeDir%\\%AppID%.cameyo.data", StringComparison.InvariantCultureIgnoreCase))
                propertyDataStorage.Text = propertyLocalStorageExeDir;   // "Under the executable's directory"
            else
                propertyDataStorage.Text = propertyLocalStorageCustom + ": " + baseDirName;
        }

        private void PropertyChange(object sender, EventArgs e)
        {
            dirty = true;
        }

        private void IsolationChanged(object sender, EventArgs e)
        {
            dirty = true;
        }

        private void lnkChangeDataStorage_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            DataStorageForm dataStorageForm = new DataStorageForm();
            if (dataStorageForm.Do(virtPackage, ref dirty))
                DisplayBaseDirName();
        }

        private void lnkChangeIcon_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = false;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                if (openFileDialog.FileName.EndsWith(".ico", StringComparison.InvariantCultureIgnoreCase))
                    MessageBox.Show("Please select an executable file (.EXE) to copy the icon from.");
                else
                {
                    Icon ico = Win32Function.getIconFromFile(openFileDialog.FileName);
                    if (virtPackage.SetIcon(openFileDialog.FileName))
                    {
                        propertyIcon.Image = ico.ToBitmap();
                        //propertyNewIconFileName.Text = openFileDialog.FileName;
                        dirty = true;
                    }
                    else
                        MessageBox.Show("Error: file not found");
                }
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            ThreadedRegLoadStop();
        }

        private void lnkAutoLaunch_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AutoLaunchForm autoLaunchForm = new AutoLaunchForm(virtPackage, fsEditor);
            String oldValue = virtPackage.GetProperty("AutoLaunch");
            if (autoLaunchForm.ShowDialog() == DialogResult.OK)
            {
                DisplayAutoLaunch();
                if (virtPackage.GetProperty("AutoLaunch") != oldValue)
                    dirty = true;
            }
        }

        // dragdrop function (DragEnter) to open a new file dropping it in the main form
        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                dragging = true;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        static public bool ExecProg(String fileName, String args, bool wait, ref int exitCode)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo(fileName, args);
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                procStartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                procStartInfo.CreateNoWindow = true;
                procStartInfo.UseShellExecute = false;
                proc.StartInfo = procStartInfo;
                proc.Start();
                if (wait)
                {
                    proc.WaitForExit();
                    exitCode = proc.ExitCode;
                }
                return true;
            }
            catch
            {
            }
            return false;
        }

        static public string PackagerExe()
        {
            if (Win64.IsWin64())
                return Path.Combine(Utils.MyPath(), "Packager64.exe");
            else
                return Path.Combine(Utils.MyPath(), "Packager.exe");
        }

        // dragdrop function (DragDrop) to open a new file dropping it in the main form
        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            dragging = false;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (files.Length > 1)
            {
                MessageBox.Show("You can drop only one file");
                return;
            }
            //if (System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(files[0]))
            //         + System.IO.Path.GetExtension(files[0]) != ".cameyo.exe")
            if (System.IO.Path.GetFileName(files[0]).IndexOf("AppVirtDll.", StringComparison.InvariantCultureIgnoreCase) != -1)
            {
                String openedFile = "";
                CloseAndReopen_Before(ref openedFile);
                try
                {
                    // Syntax: myPath\Packager.exe -ChangeEngine AppName.cameyo.exe AppVirtDll.dll
                    int exitCode = 0;
                    if (!ExecProg(PackagerExe(), "-ChangeEngine \"" + openedFile + "\" "+
                        "\"" + files[0] + "\"", true, ref exitCode))
                        MessageBox.Show("Could not execute: " + PackagerExe());
                }
                finally
                {
                    CloseAndReopn_After(openedFile);
                }
                return;
            }
            else if (System.IO.Path.GetFileName(files[0]).EndsWith("Loader.exe", StringComparison.InvariantCultureIgnoreCase))
            {
                String openedFile = "";
                CloseAndReopen_Before(ref openedFile);
                try
                {
                    // Syntax: myPath\Packager.exe -ChangeLoader AppName.cameyo.exe Loader.exe
                    string myPath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    int exitCode = 0;
                    if (!ExecProg(openedFile, "-ChangeLoader \"" + files[0] + "\"", true, ref exitCode))
                        MessageBox.Show("Could not execute: " + PackagerExe());
                }
                finally
                {
                    CloseAndReopn_After(openedFile);
                }
                return;
            }
            else if (System.IO.Path.GetExtension(files[0]).ToLower() != ".exe")
            {
                MessageBox.Show("You can only open files with .exe extension");
                return;
            }
            // open in a new thread to avoid blocking of explorer in case of big files
            this.BeginInvoke(Del_Open, new Object[] { files[0] });
            this.Activate();
        }

        // dragdrop function (DragEnter) to add file to the tree list and file list
        private void Vfs_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                ((Control)sender).Focus();
                e.Effect = DragDropEffects.Copy;
                dragging = false;
                itemHoverTimer.Interval = 2000;
                itemHoverTimer.Start();
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        // dragdrop function (DragDrop) to add file to the tree list and file list
        private void Vfs_DragDrop(object sender, DragEventArgs e)
        {
            dragging = false;
            if (itemHoverTimer.Enabled)
                itemHoverTimer.Stop();

            FolderTreeNode parentNode = (FolderTreeNode)fsFolderTree.SelectedNode;
            if (parentNode == null)
            {
                MessageBox.Show("Please select a folder to add to");
                return;
            }
            if (parentNode.deleted)
            {
                MessageBox.Show("Folder was deleted");
                return;
            }

            String[] paths = (String[])e.Data.GetData(DataFormats.FileDrop);
            foreach (String path in paths)
            {
                this.BeginInvoke(fsEditor.Del_AddFOrFR, new object[] { parentNode, path });
            }
        }

        // dragdrop function (DragOver) to add file to the tree list and file list
        private void fsFolderTree_DragOver(object sender, DragEventArgs e)
        {
            Point pt = fsFolderTree.PointToClient(new Point(e.X, e.Y));
            TreeNode nodeUnderCursor = fsFolderTree.GetNodeAt(pt);
            if (nodeUnderCursor != null)
            {
                fsFolderTree.SelectedNode = nodeUnderCursor;
            }
        }

        // dragdrop function to add file to the tree list and file list
        // this function allows the nodes to close while navigating in the tree to drop files and folders
        private void fsFolderTree_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (dragging && e.Node.Level > 0)
            {
                TreeNode oldNode = fsFolderTree.SelectedNode;
                TreeNode newNode = e.Node;
                if (oldNode.IsExpanded && newNode.Level == oldNode.Level)
                    oldNode.Collapse();
                else
                {
                    while (newNode.Level <= oldNode.Level)
                    {
                        oldNode.Collapse();
                        oldNode = oldNode.Parent;
                    }
                }
            }
        }

        // dragdrop function to add file to the tree list and file list
        // this function allows the nodes to open while navigating in the tree to drop files and folders
        private void fsFolderTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (dragging)
            {
                if (itemHoverTimer.Enabled)
                    itemHoverTimer.Stop();
                itemHoverTimer.Interval = 900;
                itemHoverTimer.Start();
            }
        }

        // timer to open the nodes
        private void OnItemHover(object sender, EventArgs e)
        {
            itemHoverTimer.Stop();
            if (fsFolderTree.SelectedNode != null)
            {
                if (!dragging)
                    dragging = true;
                fsFolderTree.SelectedNode.Expand();
            }
        }

        private void CloseAndReopen_Before(ref String openedFile)
        {
            if (this.dirty || fsEditor.dirty || regEditor.dirty)
            {
                MessageBox.Show("You have to save the package first");
                return;
            }
            openedFile = virtPackage.openedFile;
            virtPackage.Close();
        }

        private void CloseAndReopn_After(String openedFile)
        {
            if (!virtPackage.opened)
                virtPackage.Open(openedFile);
        }

        private void dropboxButton_Click(object sender, EventArgs e)
        {
#if DropBox
            String openedFile = "";
            CloseAndReopen_Before(ref openedFile);
            try
            {
                DropboxLogin dropLogin = new DropboxLogin();
                dropLogin.Publish(openedFile);
            }
            finally
            {
                CloseAndReopn_After(openedFile);
            }
#endif
        }


        private void resetCredLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            RegistryKey cameyoKey = Registry.CurrentUser.CreateSubKey(@"Software\Cameyo");
            try
            {
                cameyoKey.DeleteValue("DropBoxTokenKey");
                cameyoKey.DeleteValue("DropBoxTokenSecret");
            }
            catch
            {
                MessageBox.Show("Cannot delete login tokens, did you save them?");
            }
        }

        private void lnkCustomEvents_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            CustomEventsForm customEventsForm = new CustomEventsForm(virtPackage);
            customEventsForm.ShowDialog();
            dirty |= customEventsForm.dirty;
            customEventsForm.Dispose();
        }

        private void regFilesList_SubItemClicked(object sender, SubItemEventArgs e)
        {
            //Mario:ToDo Bugfixes:regFilesList.StartEditing(Editors[e.SubItem], e.Item, e.SubItem);
        }

        private void regFilesList_SubItemEndEditing(object sender, SubItemEndEditingEventArgs e)
        {
            Registry.SetValue(regEditor.Masterkey, regEditor.Currentkey[regFilesList.Items.IndexOf(e.Item)].ToString(), e.DisplayText);
        }

        ListViewSorter fsFilesListSorter = new FileListViewSorter();
        private void fsFilesList_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            fsFilesListSorter.Sort(fsFilesList, e.Column);
        }

        ListViewSorter regFilesListSorter = new RegListViewSorter();
        private void regFilesList_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            regFilesListSorter.Sort(regFilesList, e.Column);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            bool OK = PackageClose();
            e.Cancel = !OK;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            lblLogoTitle.Text = "Package Editor";   // Don't let this be localized!
            //MainForm_Resize(null, null);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            ListViewHelper.EnableDoubleBuffer(listViewMRU);
            Win32imports.SendMessage(listViewMRU.Handle, Win32imports.LVM_SETICONSPACING, (uint)410, (uint)410);

            foreach (MRUitem item in mru.GetItems())
            {
                if (!File.Exists(item.file))
                    continue;
                Icon ico = Win32Function.getIconFromFile(item.file);
                int imageId = imageListMRU.Images.Add(ico.ToBitmap(), Color.Empty);

                String fileName = Path.GetFileNameWithoutExtension(item.file);
                if (fileName.EndsWith(".virtual")) fileName = fileName.Remove(fileName.Length - 8);
                if (fileName.EndsWith(".cameyo")) fileName = fileName.Remove(fileName.Length - 7);

                ListViewItem lvItem = listViewMRU.Items.Add(fileName);
                lvItem.ImageIndex = imageId;
                lvItem.Tag = item.file;
                lvItem.Group = listViewMRU.Groups["recentlyEditedGroup"];
            }

            /*foreach (DeployedApp deployedApp in VirtPackage.DeployedApps())
            {
                if (!File.Exists(deployedApp.CarrierExeName))
                    continue;
                Icon ico = Win32Function.getIconFromFile(deployedApp.CarrierExeName);
                int imageId = imageListMRU.Images.Add(ico.ToBitmap(), Color.Empty);

                String fileName = deployedApp.AppID;
                if (fileName.EndsWith(".virtual")) fileName = fileName.Remove(fileName.Length - 8);
                if (fileName.EndsWith(".cameyo")) fileName = fileName.Remove(fileName.Length - 7);

                ListViewItem lvItem = listViewMRU.Items.Add(fileName);
                lvItem.ImageIndex = imageId;
                lvItem.Tag = deployedApp.CarrierExeName;
                lvItem.Group = listViewMRU.Groups["deployedAppsGroup"];
            }*/
        }

        private void rdb_CheckedChanged(object sender, EventArgs e)
        {
            bool cleanup = !rdbCleanNone.Checked;
            chkCleanAsk.Enabled = cleanup;
            chkCleanDoneDialog.Enabled = cleanup;
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!PackageClose())
                return;
            if (!virtPackage.Create("New Package ID",
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "AppVirtDll.dll"),
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Loader.exe")))
            {
                MessageBox.Show("Faild to create a new package.");
                return;
            }
            dirty = false;
            this.OnPackageOpen();
            fsEditor.OnPackageOpen();
            regEditor.OnPackageOpenBeforeUI();
            tabControl.SelectedIndex = 0;

            rdbIntegrateNone.Checked = true;
            rdbIntegrateStandard.Checked = false;
            rdbIntegrateVirtual.Checked = false;

            rdbCleanNone.Checked = true;
            rdbCleanRegOnly.Checked = false;
            rdbCleanAll.Checked = false;
            chkCleanAsk.Checked = true;
            chkCleanDoneDialog.Checked = false;

            EnableDisablePackageControls(true);
            regLoaded = true;
            regProgressTimer_Tick(null, null);
        }

        private void toolStripMenuItemExport_Click(object sender, EventArgs e)
        {
            regEditor.toolStripMenuItemExport_Click(sender, e);
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            regRemoveBtn.PerformClick();
        }

        private void tabRegistry_DragDrop(object sender, DragEventArgs e)
        {
            dragging = false;

            String[] paths = (String[])e.Data.GetData(DataFormats.FileDrop);
            foreach (String path in paths)
            {
                this.BeginInvoke(regEditor.Del_AddFOrF, new object[] { path });
            }
        }

        private void tabRegistry_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                ((Control)sender).Focus();
                e.Effect = DragDropEffects.Copy;
                dragging = false;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void tabRegistry_DragOver(object sender, DragEventArgs e)
        {
            //
        }

        private void btnNewPackage_Click(object sender, EventArgs e)
        {
            newToolStripMenuItem_Click(sender, e);
        }

        private void btnEditPackage_Click(object sender, EventArgs e)
        {
            openToolStripMenuItem_Click(sender, e);
        }

        private void listViewMRU_Click(object sender, EventArgs e)
        {
            if (listViewMRU.SelectedItems.Count != 1)
                return;
            PackageOpen((String)listViewMRU.SelectedItems[0].Tag);
        }

        private void regImportBtn_Click(object sender, EventArgs e)
        {
            regEditor.RegFileImport();
        }

        private void regExportBtn_Click(object sender, EventArgs e)
        {
            regEditor.RegFileExport();
        }

        private void fileContextMenuDelete_Click(object sender, EventArgs e)
        {
            fsRemoveBtn.PerformClick();
        }

        private void fileContextMenuProperties_Click(object sender, EventArgs e)
        {
            fsEditor.ShowProperties();
        }

        private void fsFilesList_ItemDrag(object sender, ItemDragEventArgs e)
        {
            fsEditor.DragDropFiles((FileListViewItem)e.Item);
        }

        private void fsFolderTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            fsEditor.DragDropFiles((FolderTreeNode)e.Item);
        }

        private void lnkCapture_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            int exitCode = 0;
            if (!Cameyo.OpenSrc.Common.Utils.ExecProg(PackagerExe(), null, false, ref exitCode))
                MessageBox.Show("Can't start the app packager");
            else
                Application.Exit();
        }

        private void lnkPackageEdit_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            openToolStripMenuItem_Click(this, new EventArgs());
        }

        ToolStripMenuItem CurLanguageMenuItem()
        {
            string cultureStr = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
            foreach (ToolStripMenuItem item in langToolStripMenuItem.DropDownItems)
            {
                if (((string)item.Tag).Equals(cultureStr, StringComparison.InvariantCultureIgnoreCase))
                    return item;
            }
            return englishMenuItem;
        }

        private void langMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            string cultureStr = ((string)item.Tag);
            LangUtils.SaveCulture(cultureStr);
            LangUtils.LoadCulture();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            MessageBox.Show(PackageEditor.Messages.Messages.changesWillTakeEffectOnNextStart);
        }
    }

    class RegListViewSorter : ListViewSorter
    {
        protected override int CompareItems(ListViewItem x, ListViewItem y)
        {
            return String.Compare(x.SubItems[currentcolumn].Text, y.SubItems[currentcolumn].Text);
        }
    }

    public class FileListViewItem : ListViewItem
    {
        public ulong fileSize = 0;
        public VIRT_FILE_FLAGS flags = VIRT_FILE_FLAGS.NO_FLAGS;
    }

    class FileListViewSorter : ListViewSorter
    {
        bool isFileSizeColumn = false;
        public override void Sort(ListView List, int ColumnNumber)
        {
            isFileSizeColumn = List.Columns[ColumnNumber].Text == "Size";
            base.Sort(List, ColumnNumber);
        }
        protected override int CompareItems(ListViewItem x, ListViewItem y)
        {
            int res;
            if (isFileSizeColumn)
                res = ((FileListViewItem)x).fileSize.CompareTo(((FileListViewItem)y).fileSize);
            else
                res = String.Compare(x.SubItems[currentcolumn].Text, y.SubItems[currentcolumn].Text);
            return res;
        }
    }

    abstract class ListViewSorter : IComparer
    {
        protected int currentcolumn = -1;
        bool currentAsc = true;
        public virtual void Sort(ListView List, int ColumnNumber)
        {
            if (currentcolumn == ColumnNumber)
            {
                currentAsc = !currentAsc;
            }
            else
                currentAsc = true;
            currentcolumn = ColumnNumber;
            List.ListViewItemSorter = this;
            List.Sort();
        }
        public int Compare(object x, object y)
        {
            int res = CompareItems((ListViewItem)x, (ListViewItem)y);
            if (!currentAsc)
                res = -res;
            return res;
        }
        abstract protected int CompareItems(ListViewItem x, ListViewItem y);
    }

    public class MRUitem
    {
        public MRUitem(String name, String file)
        {
            this.name = name;
            this.file = file;
        }
        public String name;
        public String file;
    }

    public class MRU
    {
        public int maxItems;
        private RegistryKey regKey;

        public MRU(String regKeyName)
        {
            maxItems = 8;
            try
            {
                regKey = Registry.CurrentUser.OpenSubKey(regKeyName, true);
                if (regKey == null)
                    regKey = Registry.CurrentUser.CreateSubKey(regKeyName);
                if (regKey == null)
                {
                    MessageBox.Show("Cannot write to registry. Quitting.", "Fatal");
                    Application.Exit();
                }
            }
            catch { }
        }

        ~MRU()
        {
            regKey.Close();
        }

        private void InsertTopItem(String fileName)
        {
            // Delete maximum entry, if it exists
            regKey.DeleteValue(Convert.ToString(maxItems - 1), false);

            // Look for empty holes (or last element) to move all items up to
            int moveTo = -1;
            for (int i = 0; i < maxItems; i++)
            {
                String fileNameValue = (String)regKey.GetValue(Convert.ToString(i));
                if (fileNameValue == null)
                {
                    moveTo = i;
                    break;
                }
            }

            // Rename (increment) all items up to moveTo
            if (moveTo > 0)
            {
                for (int i = moveTo - 1; i >= 0; i--)
                {
                    // Rename #1:filename -> #2:filename
                    String fileNameValue = (String)regKey.GetValue(Convert.ToString(i));
                    if (i < maxItems)
                        regKey.SetValue(Convert.ToString(i + 1), fileNameValue);
                }
            }

            // Add "0" item
            try { regKey.SetValue("0", fileName); }
            catch { }
        }

        public List<MRUitem> GetItems()
        {
            List<MRUitem> result = new List<MRUitem>();

            String[] items = regKey.GetValueNames();
            List<String> list = new List<string>(items);
            list.Sort();
            for (int i = 0; i < list.Count; i++)
            {
                String fileNameValue = (String)regKey.GetValue(list[i]);
                if (fileNameValue == null || !File.Exists(fileNameValue))
                    continue;
                result.Add(new MRUitem(list[i], fileNameValue));
            }
            return result;
        }

        public void AddFile(String fileName)
        {
            // First browse through MRU items, deleting those that already contain this fileName
            String[] items = regKey.GetValueNames();
            for (int i = 0; i < items.Length; i++)
            {
                String fileNameValue = (String)regKey.GetValue(items[i]);
                if (fileNameValue == null)
                    continue;
                if (fileNameValue.ToUpper() == fileName.ToUpper())
                    regKey.DeleteValue(items[i]);
            }

            // Add to top ("0")
            InsertTopItem(fileName);
        }
    }
}
