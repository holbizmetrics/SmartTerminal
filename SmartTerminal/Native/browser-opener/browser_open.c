/*
 * browser_open.c -> libbrowseropener.so
 *
 * Claude Code's browser-open (cli.js J3) runs `$BROWSER <url>`, else xdg-open —
 * which doesn't exist on Android, so OAuth login prints "Browser didn't open?".
 * We set BROWSER=<this> (in nativeLibraryDir, the only exec-allowed dir), so claude
 * hands us the URL and we open it via Android's am -> the system browser. No copy,
 * no paste, no wrapped-URL corruption. Ships as lib*.so (SELinux exec constraint).
 *
 * claude calls it with exactly one arg (the URL). We exec am with a VIEW intent.
 */
#include <unistd.h>

int main(int argc, char **argv) {
    if (argc < 2) return 1;
    execl("/system/bin/am", "am", "start",
          "-a", "android.intent.action.VIEW",
          "-d", argv[1], (char *)0);
    return 127; /* exec failed */
}
