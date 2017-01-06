namespace TvFox
{
    sealed partial class VideoForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.overlayTopLeft = new System.Windows.Forms.Label();
            this.mainTimer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // overlayTopLeft
            // 
            this.overlayTopLeft.AutoSize = true;
            this.overlayTopLeft.BackColor = System.Drawing.Color.Black;
            this.overlayTopLeft.Font = new System.Drawing.Font("Consolas", 22F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.overlayTopLeft.ForeColor = System.Drawing.Color.DarkGreen;
            this.overlayTopLeft.Location = new System.Drawing.Point(13, 13);
            this.overlayTopLeft.Name = "overlayTopLeft";
            this.overlayTopLeft.Size = new System.Drawing.Size(238, 51);
            this.overlayTopLeft.TabIndex = 0;
            this.overlayTopLeft.Text = "Bark Bark";
            this.overlayTopLeft.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // mainTimer
            // 
            this.mainTimer.Enabled = true;
            this.mainTimer.Tick += new System.EventHandler(this.mainTimer_Tick);
            // 
            // VideoForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 346);
            this.Controls.Add(this.overlayTopLeft);
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.Name = "VideoForm";
            this.Text = "TvFox";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Label overlayTopLeft;
        private System.Windows.Forms.Timer mainTimer;
    }
}