using System.Drawing;
using System.Windows.Forms;

namespace InspectionWidget
{
    /// <summary>
    /// 主面板调用的缩放内置弹窗（构造前由调用方经 UiTheme.UseFontScale 对齐字体缩放）：
    /// 进度损坏恢复、重置本轮确认与全部完成询问。
    /// </summary>
    internal static class DialogCardHelper
    {
        public static Panel CreateBodyHost(Color accentStripeColor, string bodyText)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackColor,
                Padding = new Padding(14)
            };
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(17, 14, 14, 14)
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Panel)sender;
                if (p.Width <= 2 || p.Height <= 2) return;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
                int barW = System.Math.Max(3, p.Padding.Left / 5);
                using (var brush = new SolidBrush(accentStripeColor))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, barW, p.Height);
                }
            };
            var message = new Label
            {
                Text = bodyText,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor
            };
            card.Controls.Add(message);
            host.Controls.Add(card);
            return host;
        }

        public static FlowLayoutPanel CreateButtonDock()
        {
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(14, 11, 14, 11)
            };
            buttons.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 0, 0, p.Width, 0);
                }
            };
            return buttons;
        }
    }

    internal sealed class ProgressRecoveryDialog : Form
    {
        public ProgressRecoveryDialog(string path, string error)
        {
            Text = "巡检进度恢复";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(468, 244);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            string body = "进度文件读取或校验失败，已暂停覆盖原文件：\n"
                + (error ?? "未知错误")
                + (string.IsNullOrEmpty(path) ? string.Empty : "\n\n文件位置：" + path)
                + "\n\n你可以先检查或替换文件后「重试读取」，或将坏原件备份后再「备份并重置」开启今日新轮。";
            var content = DialogCardHelper.CreateBodyHost(UiTheme.ErrorColor, body);
            var buttons = DialogCardHelper.CreateButtonDock();

            var keep = UiTheme.SecondaryButton("保留原件");
            keep.Width = 92;
            keep.Margin = new Padding(0);
            keep.DialogResult = DialogResult.Cancel;
            var backupReset = UiTheme.SecondaryButton("备份并重置");
            backupReset.ForeColor = UiTheme.ErrorColor;
            backupReset.Width = 108;
            backupReset.Margin = new Padding(0, 0, 8, 0);
            backupReset.DialogResult = DialogResult.Yes;
            var retry = UiTheme.PrimaryButton("重试读取");
            retry.Width = 96;
            retry.Margin = new Padding(0, 0, 8, 0);
            retry.DialogResult = DialogResult.Retry;
            buttons.Controls.Add(keep);
            buttons.Controls.Add(backupReset);
            buttons.Controls.Add(retry);
            Controls.Add(content);
            Controls.Add(buttons);
            AcceptButton = retry;
            CancelButton = keep;
        }
    }

    internal sealed class ResetRoundConfirmDialog : Form
    {
        public ResetRoundConfirmDialog()
        {
            Text = "重置本轮进度";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(428, 168);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            var content = DialogCardHelper.CreateBodyHost(
                UiTheme.ErrorColor,
                "确定清空今日“已点过”记录并重新开始本轮吗？（不会删除截图文件）");
            var buttons = DialogCardHelper.CreateButtonDock();

            var confirm = UiTheme.DangerButton("确定重置");
            confirm.Width = 104;
            confirm.Margin = new Padding(0);
            confirm.DialogResult = DialogResult.Yes;
            var cancel = UiTheme.SecondaryButton("取消");
            cancel.Width = 84;
            cancel.Margin = new Padding(0, 0, 8, 0);
            cancel.DialogResult = DialogResult.No;
            buttons.Controls.Add(confirm);
            buttons.Controls.Add(cancel);
            Controls.Add(content);
            Controls.Add(buttons);
            AcceptButton = cancel;
            CancelButton = cancel;
        }
    }

    internal sealed class CompletionPromptDialog : Form
    {
        public CompletionPromptDialog(bool hasPendingFailure, bool hasSavedFiles, string progressSaveError)
        {
            Text = "今日巡检进度";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(448, string.IsNullOrEmpty(progressSaveError) ? 182 : 240);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            string body;
            Color stripe;
            if (hasPendingFailure)
            {
                stripe = UiTheme.WarningColor;
                body = "今日所有设备均已点过，但当前仍有截图尚未保存成功，请先点击底部反馈区重试。\n\n是否仍要打开巡检文件夹？";
            }
            else if (!hasSavedFiles)
            {
                stripe = UiTheme.WarningColor;
                body = "今日所有设备均已点过，但本轮尚未记录到已保存的截图文件。\n\n是否检查并打开巡检文件夹？";
            }
            else
            {
                stripe = UiTheme.SuccessColor;
                body = "今日所有设备均已点过。\n\n是否立即打开巡检文件夹？";
            }
            if (!string.IsNullOrEmpty(progressSaveError))
            {
                body += "\n\n注意：本次“不再重复提醒”标记未能写入进度文件（"
                    + progressSaveError + "），下次重启后可能再次提示。";
            }

            var content = DialogCardHelper.CreateBodyHost(stripe, body);
            var buttons = DialogCardHelper.CreateButtonDock();

            var open = UiTheme.PrimaryButton("打开文件夹");
            open.Width = 116;
            open.Margin = new Padding(0);
            open.DialogResult = DialogResult.Yes;
            var later = UiTheme.SecondaryButton("稍后");
            later.Width = 84;
            later.Margin = new Padding(0, 0, 8, 0);
            later.DialogResult = DialogResult.No;
            buttons.Controls.Add(open);
            buttons.Controls.Add(later);
            Controls.Add(content);
            Controls.Add(buttons);
            AcceptButton = open;
            CancelButton = later;
        }
    }
}
