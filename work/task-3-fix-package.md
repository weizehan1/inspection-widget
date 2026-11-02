# Re-review Task 3 fix: a3cd297..HEAD
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 9d93b4d..9e878de 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -721,17 +721,7 @@ namespace InspectionWidget
                 case ResolveReason.Excluded:
                     ShowFeedback("点到桌面/任务栏，已忽略，请点目标窗口", true);
                     // 保持 Waiting 并重启钩子（新一轮 30 秒超时）
-                    _hooks.Stop();
-                    try
-                    {
-                        _hooks.Start(this.Bounds);
-                    }
-                    catch (Exception ex)
-                    {
-                        _stateMachine.Cancel();
-                        OnStateChanged();
-                        ShowFeedback("选窗启动失败：" + ex.Message, true);
-                    }
+                    RestartHooksWhileWaiting();
                     break;
                 case ResolveReason.Minimized:
                     ShowFeedback("该窗口已最小化，请先打开", true);
@@ -746,21 +736,32 @@ namespace InspectionWidget
                     break;
                 default: // Gone
                     ShowFeedback("该窗口已关闭，请重新点选", true);
-                    _hooks.Stop();
-                    try
-                    {
-                        _hooks.Start(this.Bounds);
-                    }
-                    catch (Exception ex)
-                    {
-                        _stateMachine.Cancel();
-                        OnStateChanged();
-                        ShowFeedback("选窗启动失败：" + ex.Message, true);
-                    }
+                    RestartHooksWhileWaiting();
                     break;
             }
         }
 
+        /// <summary>
+        /// 保持 Waiting 并重启钩子（新一轮 30 秒超时）：Excluded 与 Gone 两分支共用。
+        /// 先 Stop 再 Start；Start 失败时 Cancel 状态机回 Idle、经既有集中入口
+        /// OnStateChanged 停钩并刷新视觉，再给出失败反馈，与原两分支内联逻辑
+        /// 的语义完全一致（OnStateChanged 在非 Waiting 时亦会 Stop，幂等无副作用）。
+        /// </summary>
+        private void RestartHooksWhileWaiting()
+        {
+            _hooks.Stop();
+            try
+            {
+                _hooks.Start(this.Bounds);
+            }
+            catch (Exception ex)
+            {
+                _stateMachine.Cancel();
+                OnStateChanged();
+                ShowFeedback("选窗启动失败：" + ex.Message, true);
+            }
+        }
+
         private void OnHookCancelRequested()
         {
             if (InvokeRequired)
