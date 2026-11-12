using System;

namespace InspectionWidget.Config
{
    /// <summary>
    /// 设置提交编排：核对草稿代次、导入恢复备份与正式保存一次完成。
    /// 成功结果只返回一次，失败不修改任何已保存基线。
    /// </summary>
    public static class SettingsApplyCoordinator
    {
        public sealed class Outcome
        {
            public bool Applied;
            public AppConfig Result;
            public SaveResult Save;
            public string Error;
            public bool Stale;
            public bool Retryable;
        }

        public static Outcome Execute(AppConfig candidate, ConfigStore store,
            string expectedRevision, bool importPending)
        {
            if (candidate == null)
                return Fail("设置未保存：候选配置为空。");
            if (store == null)
                return Fail("设置未保存：配置存储不可用。");
            // 设置提交必须带有效基线：null 无法证明草稿基于当前文件，
            // 失败读取另用不可提交标识区分，两者都不能进入写盘。
            if (expectedRevision == null
                || ConfigStore.IsUnusableRevision(expectedRevision))
                return Fail("设置未保存：配置基线无效，请重新读取配置后再提交。");
            if (store.WriteProtection.IsProtected)
                return Fail("设置未保存：配置处于读取失败保护状态，请重新读取配置后再提交。");

            SaveResult save;
            try
            {
                save = store.Save(candidate, new ConfigSaveOptions
                {
                    ExpectedRevision = expectedRevision,
                    CreatePreImportBackup = importPending
                });
            }
            catch (Exception ex)
            {
                return Fail("设置未保存：" + ex.Message);
            }

            if (save == null)
                return Fail("设置未保存：未知错误。");
            if (!save.Saved)
            {
                return new Outcome
                {
                    Applied = false,
                    Save = save,
                    Error = save.Error,
                    Stale = save.Stale,
                    Retryable = save.Retryable
                };
            }
            return new Outcome
            {
                Applied = true,
                Result = candidate,
                Save = save
            };
        }

        private static Outcome Fail(string error)
        {
            return new Outcome { Applied = false, Error = error };
        }
    }
}
