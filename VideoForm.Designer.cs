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
            this.overlayTopRight = new System.Windows.Forms.Label();
            this.overlayBottomCenter = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // overlayTopLeft
            // 
            this.overlayTopLeft.AutoSize = true;
            this.overlayTopLeft.BackColor = System.Drawing.Color.Black;
            this.overlayTopLeft.Font = new System.Drawing.Font("Consolas", 22F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.overlayTopLeft.ForeColor = System.Drawing.Color.Lime;
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
            this.mainTimer.Tick += new System.EventHandler(this.HandleTimerTick);
            // 
            // overlayTopRight
            // 
            this.overlayTopRight.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.overlayTopRight.AutoSize = true;
            this.overlayTopRight.BackColor = System.Drawing.Color.Black;
            this.overlayTopRight.Font = new System.Drawing.Font("Consolas", 22F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.overlayTopRight.ForeColor = System.Drawing.Color.Lime;
            this.overlayTopRight.Location = new System.Drawing.Point(1047, 13);
            this.overlayTopRight.Name = "overlayTopRight";
            this.overlayTopRight.Size = new System.Drawing.Size(118, 51);
            this.overlayTopRight.TabIndex = 1;
            this.overlayTopRight.Text = "MUTE";
            this.overlayTopRight.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // overlayBottomCenter
            // 
            this.overlayBottomCenter.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.overlayBottomCenter.AutoSize = true;
            this.overlayBottomCenter.BackColor = System.Drawing.Color.Black;
            this.overlayBottomCenter.Font = new System.Drawing.Font("Consolas", 22F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.overlayBottomCenter.ForeColor = System.Drawing.Color.Lime;
            this.overlayBottomCenter.Location = new System.Drawing.Point(449, 500);
            this.overlayBottomCenter.Name = "overlayBottomCenter";
            this.overlayBottomCenter.Size = new System.Drawing.Size(334, 51);
            this.overlayBottomCenter.TabIndex = 2;
            this.overlayBottomCenter.Text = "WWWWWWWWWWWWW";
            this.overlayBottomCenter.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // VideoForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1177, 644);
            this.Controls.Add(this.overlayBottomCenter);
            this.Controls.Add(this.overlayTopRight);
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
        public System.Windows.Forms.Label overlayTopRight;
        public System.Windows.Forms.Label overlayBottomCenter;
    }
}