//   !!  // TVFox - https://github.com/FoxCouncil/TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System.Reflection;
using System.Windows.Forms;
using TVFox.Windows;

namespace TVFox
{
    partial class AboutForm : Form
    {
        private bool _wasMouseHidden;
        private bool _wasAlwaysOnTop;

        public AboutForm()
        {
            InitializeComponent();

            Text = $"About {AssemblyTitle}";
            
            labelProductName.Text = AssemblyProduct;
            labelVersion.Text = $"Version {AssemblyVersion}";
            labelCopyright.Text = AssemblyCopyright;
            labelCompanyName.Text = AssemblyCompany;
            textBoxDescription.Text = AssemblyDescription;

            VisibleChanged += (sender, args) =>
            {
                if (Visible && Utilities.IsWindowAlwaysOnTop(TVFoxApp.VideoWindow?.Handle))
                {
                    _wasAlwaysOnTop = true;

                    Utilities.SetAlwaysOnTop(TVFoxApp.VideoWindow?.Handle, false);
                }
                else if (!Visible && _wasAlwaysOnTop)
                {
                    Utilities.SetAlwaysOnTop(TVFoxApp.VideoWindow?.Handle, true);

                    _wasAlwaysOnTop = false;
                }

                if (Visible && !Utilities.IsMouseVisible)
                {
                    _wasMouseHidden = true;

                    Utilities.SetMouseVisibility(true);
                }
                else if (!Visible && _wasMouseHidden)
                {
                    Utilities.SetMouseVisibility(false);

                    _wasMouseHidden = false;
                }
            };
        }

        #region Assembly Attribute Accessors

        public string AssemblyTitle
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);

                if (attributes.Length > 0)
                {
                    AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];

                    if (titleAttribute.Title != "")
                    {
                        return titleAttribute.Title;
                    }
                }

                return "TVFox";
            }
        }

        public string AssemblyVersion
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        public string AssemblyDescription
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyDescriptionAttribute)attributes[0]).Description;
            }
        }

        public string AssemblyProduct
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyProductAttribute)attributes[0]).Product;
            }
        }

        public string AssemblyCopyright
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
            }
        }

        public string AssemblyCompany
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCompanyAttribute)attributes[0]).Company;
            }
        }
        #endregion
    }
}
