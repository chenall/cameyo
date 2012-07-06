using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
//using System.Linq;
using System.Text;
using System.Windows.Forms;
using VirtPackageAPI;

namespace PackageEditor
{
    public partial class DataStorageForm : Form
    {
        public DataStorageForm()
        {
            InitializeComponent();
        }

        public bool Do(VirtPackage virtPackage, ref bool dirty)
        {
            String oldValue = virtPackage.GetProperty("BaseDirName");
            String newValue;
            propertyLocalStorageCustomDir.Text = "";
            if (oldValue == "")
                propertyLocalStorageDefault.Checked = true;
            else if (oldValue.Equals("%ExeDir%\\%AppID%.cameyo.data", StringComparison.InvariantCultureIgnoreCase))
                propertyLocalStorageExeDir.Checked = true;
            else
            {
                propertyLocalStorageCustom.Checked = true;
                propertyLocalStorageCustomDir.Text = oldValue;
            }
            if (ShowDialog() == DialogResult.OK)
            {
                if (propertyLocalStorageDefault.Checked)
                    newValue = "";
                else if (propertyLocalStorageExeDir.Checked)
                    newValue = "%ExeDir%\\%AppID%.cameyo.data";
                else
                    newValue = propertyLocalStorageCustomDir.Text;
                if (newValue != oldValue)
                {
                    virtPackage.SetProperty("BaseDirName", newValue);
                    dirty = true;
                }
                return true;
            }
            else
                return false;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }
}
