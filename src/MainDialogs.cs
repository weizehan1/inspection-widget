using System.Drawing;
using System.Windows.Forms;

namespace InspectionWidget
{
    /// <summary>
    /// 主面板调用的缩放内置弹窗（构造前由调用方经 UiTheme.UseFontScale 对齐字体缩放）：
    /// 进度损坏恢复、重置本轮确认与全部完成询问。
    /// </summary>
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
            ClientSize = new Size(460, 200);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            string body = "进度文件读取或校验失败，已暂停覆盖原文件：\n"
                + (error ?? "未知错误")
                + (string.IsNullOrEmpty(path) ? string.Empty : "\n\n文件位置：" + path)
                + "\n\n你可以先检查或替换文件后「重试读取」，或将坏原件备份后再「备份并重置」开启今日新轮。";
            var message = new Label
            {
                Text = body,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ForeColor = UiTheme.PrimaryTextColor
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12)
            };
            var keep = UiTheme.SecondaryButton("保留原件");
            keep.Width = 92;
            keep.DialogResult = DialogResult.Cancel;
            var backupReset = UiTheme.SecondaryButton("备份并重置");
            backupReset.ForeColor = UiTheme.ErrorColor;
            backupReset.Width = 108;
            backupReset.DialogResult = DialogResult.Yes;
            var retry = UiTheme.PrimaryButton("重试读取");
            retry.Width = 96;
            retry.DialogResult = DialogResult.Retry;
            buttons.Controls.Add(keep);
            buttons.Controls.Add(backupReset);
            buttons.Controls.Add(retry);
            Controls.Add(message);
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
            ClientSize = new Size(420, 148);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            var message = new Label
            {
                Text = "确定清空今日所有设备的“已点过”记录并重新开始本轮吗？\n\n已保存的截图文件不会被删除。",
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ForeColor = UiTheme.PrimaryTextColor
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12)
            };
            var confirm = UiTheme.PrimaryButton("确定重置");
            confirm.BackColor = UiTheme.ErrorColor;
            confirm.Width = 104;
            confirm.DialogResult = DialogResult.Yes;
            var cancel = UiTheme.SecondaryButton("取消");
            cancel.Width = 84;
            cancel.DialogResult = DialogResult.No;
            buttons.Controls.Add(confirm);
            buttons.Controls.Add(cancel);
            Controls.Add(message);
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
            ClientSize = new Size(440, string.IsNullOrEmpty(progressSaveError) ? 160 : 210);
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;

            string body;
            if (hasPendingFailure)
            {
                body = "今日所有设备均已点过，但当前仍有截图尚未保存成功，请先点击底部反馈区重试。\n\n是否仍要打开巡检文件夹？";
            }
            else if (!hasSavedFiles)
            {
                body = "今日所有设备均已点过，但本轮尚未记录到已保存的截图文件。\n\n是否检查并打开巡检文件夹？";
            }
            else
            {
                body = "今日所有设备均已点过。\n\n是否立即打开巡检文件夹？";
            }
            if (!string.IsNullOrEmpty(progressSaveError))
            {
                body += "\n\n注意：本次“不再重复提醒”标记未能写入进度文件（"
                    + progressSaveError + "），下次重启后可能再次提示。";
            }

            var message = new Label
            {
                Text = body,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ForeColor = UiTheme.PrimaryTextColor
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12)
            };
            var open = UiTheme.PrimaryButton("打开文件夹");
            open.Width = 116;
            open.DialogResult = DialogResult.Yes;
            var later = UiTheme.SecondaryButton("稍后");
            later.Width = 84;
            later.DialogResult = DialogResult.No;
            buttons.Controls.Add(open);
            buttons.Controls.Add(later);
            Controls.Add(message);
            Controls.Add(buttons);
            AcceptButton = open;
            CancelButton = later;
        }
    }
}
