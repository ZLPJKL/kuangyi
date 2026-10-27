using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace KuangYi.App.Services;

public static class HotkeyGesture
{
	public static bool TryParse(string? text, out ParsedHotkey parsed, out string? error)
	{
		parsed = new ParsedHotkey(0u, 0u);
		error = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			error = "快捷键不能为空";
			return false;
		}
		string[] array = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0)
		{
			error = "快捷键格式不正确";
			return false;
		}
		uint num = 0u;
		for (int i = 0; i < array.Length - 1; i++)
		{
			uint num2;
			switch (array[i].ToUpperInvariant())
			{
			case "CTRL":
			case "CONTROL":
				num2 = 2u;
				break;
			case "ALT":
				num2 = 1u;
				break;
			case "SHIFT":
				num2 = 4u;
				break;
			case "META":
			case "WINDOWS":
			case "WIN":
				num2 = 8u;
				break;
			default:
				num2 = 0u;
				break;
			}
			uint num3 = num2;
			if (num3 == 0)
			{
				error = "无法识别的修饰键：" + array[i];
				return false;
			}
			num |= num3;
		}
		string text2 = array[^1];
		Key result;
		if (text2.Length == 1 && char.IsAsciiDigit(text2[0]))
		{
			result = (Key)(34 + (text2[0] - 48));
		}
		else if (!Enum.TryParse<Key>(text2, ignoreCase: true, out result))
		{
			error = "无法识别的按键：" + text2;
			return false;
		}
		uint num4 = (uint)KeyInterop.VirtualKeyFromKey(result);
		if (num4 == 0)
		{
			error = "该按键不支持注册为全局热键";
			return false;
		}
		int num5 = (int)result;
		bool flag = num5 >= 90 && num5 <= 113;
		if (num == 0 && !flag)
		{
			error = "字母或数字热键必须搭配 Ctrl / Alt / Shift / Win";
			return false;
		}
		if ((uint)(result - 70) <= 1u)
		{
			error = "不能使用 Win 键本身作为热键";
			return false;
		}
		parsed = new ParsedHotkey(num, num4);
		return true;
	}

	public static string Format(Key key, ModifierKeys modifiers)
	{
		List<string> list = new List<string>();
		if (modifiers.HasFlag(ModifierKeys.Control))
		{
			list.Add("Ctrl");
		}
		if (modifiers.HasFlag(ModifierKeys.Alt))
		{
			list.Add("Alt");
		}
		if (modifiers.HasFlag(ModifierKeys.Shift))
		{
			list.Add("Shift");
		}
		if (modifiers.HasFlag(ModifierKeys.Windows))
		{
			list.Add("Win");
		}
		list.Add(key.ToString());
		return string.Join("+", list);
	}

	public static bool IsModifierKey(Key key)
	{
		if ((uint)(key - 70) <= 1u || (uint)(key - 116) <= 5u)
		{
			return true;
		}
		return false;
	}
}
