using System;
using System.Collections.Generic;
//using System.Linq;
using System.Windows.Forms;

namespace PackageEditor
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Cameyo.OpenSrc.Common.LangUtils.LoadCulture();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            String param = "";
            bool notifyPackageBuilt = false;
            if (args.Length >= 1)
            {
                if (args.Length == 2 && args[0].Equals("/packagebuilt", StringComparison.InvariantCultureIgnoreCase))
                {
                    notifyPackageBuilt = true;
                    param = args[1];
                }
                else if (args.Length == 2 && args[0].Equals("/edit", StringComparison.InvariantCultureIgnoreCase))
                {
                    param = args[1];
                }
                else
                    param = args[0];
            }
            Application.Run(new MainForm(param, notifyPackageBuilt));
        }
    }
}
