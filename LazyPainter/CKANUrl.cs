using System;
using UnityEngine;

namespace LazyPainter;

public static class CKANUrl
{
    public static bool useInstallGrammar = false;

    public static void Install(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        string id = identifier.Trim();
        string url = useInstallGrammar
            ? "ckan://install?mod=" + Uri.EscapeDataString(id)
            : "ckan://" + id;

        Debug.Log($"[LazyPainter]: Opening CKAN URL: {url}");
        Application.OpenURL(url);
    }
}
