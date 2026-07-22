using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class HotKeyBindingServiceTests
{
    public static void Run()
    {
        DefaultsUseControlAltFunctionKeys();
        FormatsModifiersInStableOrder();
        RejectsBindingsWithoutModifiers();
        RejectsModifierAliasesStoredAsKeys();
        RejectsDuplicateBindings();
        FailedReplacementRestoresPreviousRegistrations();
        FailedUnregisterRestoresTheCompletePreviousSet();
        FailedReplacementTracksPartialRestore();
    }

    private static void DefaultsUseControlAltFunctionKeys()
    {
        TestAssert.Equal(
            "Ctrl+Alt+F8",
            HotKeyBindingService.Format(HotKeyBindingService.DefaultCapture),
            "采点默认快捷键应减少与单独功能键的冲突");
        TestAssert.Equal(
            "Ctrl+Alt+F9",
            HotKeyBindingService.Format(HotKeyBindingService.DefaultStartPause),
            "开始暂停默认快捷键应减少与单独功能键的冲突");
        TestAssert.Equal(
            "Ctrl+Alt+F10",
            HotKeyBindingService.Format(HotKeyBindingService.DefaultStop),
            "停止默认快捷键应减少与单独功能键的冲突");
    }

    private static void FormatsModifiersInStableOrder()
    {
        var binding = new HotKeyBinding(
            Keys.F6,
            ShortcutModifiers.Windows
            | ShortcutModifiers.Shift
            | ShortcutModifiers.Alt
            | ShortcutModifiers.Control);

        TestAssert.Equal(
            "Ctrl+Alt+Shift+Win+F6",
            HotKeyBindingService.Format(binding),
            "快捷键显示顺序应稳定");
    }

    private static void RejectsBindingsWithoutModifiers()
    {
        var valid = HotKeyBindingService.Validate(
            new HotKeyBinding(Keys.F8, ShortcutModifiers.None),
            out var error);

        TestAssert.True(!valid, "单独功能键应被拒绝");
        TestAssert.True(error.Contains("修饰键"), "错误信息应说明需要修饰键");
    }

    private static void RejectsModifierAliasesStoredAsKeys()
    {
        var valid = HotKeyBindingService.Validate(
            new HotKeyBinding(
                Keys.Control,
                ShortcutModifiers.Control | ShortcutModifiers.Alt),
            out var error);

        TestAssert.True(!valid, "设置文件中的修饰键别名不能作为主键");
        TestAssert.True(error.Contains("主键"), "错误信息应说明主键无效");
    }

    private static void RejectsDuplicateBindings()
    {
        var duplicate = new HotKeyBinding(
            Keys.F8,
            ShortcutModifiers.Control | ShortcutModifiers.Alt);

        var valid = HotKeyBindingService.ValidateSet(
            duplicate,
            duplicate,
            HotKeyBindingService.DefaultStop,
            out var error);

        TestAssert.True(!valid, "三个操作不能配置重复快捷键");
        TestAssert.True(error.Contains("重复"), "错误信息应说明快捷键重复");
    }

    private static void FailedReplacementRestoresPreviousRegistrations()
    {
        var registrar = new FakeHotKeyRegistrar();
        var coordinator = new HotKeyRegistrationCoordinator(registrar);
        var original = CreateRegistrations(
            HotKeyBindingService.DefaultCapture,
            HotKeyBindingService.DefaultStartPause,
            HotKeyBindingService.DefaultStop);
        var replacement = CreateRegistrations(
            new HotKeyBinding(Keys.F6, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F7, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F8, ShortcutModifiers.Control | ShortcutModifiers.Shift));

        var initialResult = coordinator.RegisterInitial(original);
        registrar.Events.Clear();
        registrar.FailOnBinding = replacement[1].Binding;

        var result = coordinator.Replace(replacement);

        TestAssert.True(initialResult.Success, "初始快捷键应注册成功");
        TestAssert.True(!result.Success, "新快捷键部分冲突时替换应失败");
        TestAssert.Equal(
            "unregister:1001|unregister:1002|unregister:1003|"
            + "register:1001:Ctrl+Shift+F6|register:1002:Ctrl+Shift+F7|"
            + "unregister:1001|"
            + "register:1001:Ctrl+Alt+F8|register:1002:Ctrl+Alt+F9|register:1003:Ctrl+Alt+F10",
            string.Join("|", registrar.Events),
            "注册失败后应释放部分新快捷键并恢复旧快捷键");
        TestAssert.Equal(
            "Ctrl+Alt+F8",
            HotKeyBindingService.Format(coordinator.RegisteredBindings[0].Binding),
            "失败后协调器应保留原注册状态");
    }

    private static void FailedUnregisterRestoresTheCompletePreviousSet()
    {
        var registrar = new FakeHotKeyRegistrar();
        var coordinator = new HotKeyRegistrationCoordinator(registrar);
        var original = CreateRegistrations(
            HotKeyBindingService.DefaultCapture,
            HotKeyBindingService.DefaultStartPause,
            HotKeyBindingService.DefaultStop);
        var replacement = CreateRegistrations(
            new HotKeyBinding(Keys.F6, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F7, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F8, ShortcutModifiers.Control | ShortcutModifiers.Shift));

        coordinator.RegisterInitial(original);
        registrar.Events.Clear();
        registrar.FailOnUnregisterIds.Add(1002);

        var result = coordinator.Replace(replacement);

        TestAssert.True(!result.Success, "旧快捷键注销失败时不能继续替换");
        TestAssert.True(
            registrar.Events.All(item => !item.Contains("Ctrl+Shift")),
            "旧快捷键未完整注销时不应注册任何新组合");
        TestAssert.Equal(
            3,
            coordinator.RegisteredBindings.Count,
            "注销失败后应恢复完整旧快捷键集合");
    }

    private static void FailedReplacementTracksPartialRestore()
    {
        var registrar = new FakeHotKeyRegistrar();
        var coordinator = new HotKeyRegistrationCoordinator(registrar);
        var original = CreateRegistrations(
            HotKeyBindingService.DefaultCapture,
            HotKeyBindingService.DefaultStartPause,
            HotKeyBindingService.DefaultStop);
        var replacement = CreateRegistrations(
            new HotKeyBinding(Keys.F6, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F7, ShortcutModifiers.Control | ShortcutModifiers.Shift),
            new HotKeyBinding(Keys.F8, ShortcutModifiers.Control | ShortcutModifiers.Shift));

        coordinator.RegisterInitial(original);
        registrar.FailOnBindings.Add(replacement[1].Binding);
        registrar.FailOnBindings.Add(original[2].Binding);

        var result = coordinator.Replace(replacement);

        TestAssert.True(!result.Success, "新组合冲突时替换应失败");
        TestAssert.True(
            result.Message.Contains("恢复停止快捷键失败"),
            "部分恢复失败应明确报告具体操作");
        TestAssert.Equal(
            2,
            coordinator.RegisteredBindings.Count,
            "协调器应准确记录实际恢复成功的快捷键");
    }

    private static HotKeyRegistration[] CreateRegistrations(
        HotKeyBinding capture,
        HotKeyBinding startPause,
        HotKeyBinding stop)
    {
        return
        [
            new HotKeyRegistration(1001, "采点", capture),
            new HotKeyRegistration(1002, "开始/暂停", startPause),
            new HotKeyRegistration(1003, "停止", stop)
        ];
    }

    private sealed class FakeHotKeyRegistrar : IHotKeyRegistrar
    {
        public List<string> Events { get; } = [];

        public HashSet<HotKeyBinding> FailOnBindings { get; } = [];

        public HashSet<int> FailOnUnregisterIds { get; } = [];

        public HotKeyBinding? FailOnBinding
        {
            set
            {
                if (value is not null)
                {
                    FailOnBindings.Add(value);
                }
            }
        }

        public void Register(HotKeyRegistration registration)
        {
            Events.Add(
                $"register:{registration.Id}:{HotKeyBindingService.Format(registration.Binding)}");
            if (FailOnBindings.Contains(registration.Binding))
            {
                throw new InvalidOperationException("快捷键已被占用");
            }
        }

        public void Unregister(int id)
        {
            Events.Add($"unregister:{id}");
            if (FailOnUnregisterIds.Contains(id))
            {
                throw new InvalidOperationException("注销快捷键失败");
            }
        }

        public void Dispose()
        {
        }
    }
}
