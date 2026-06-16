# Agent Guidance

## Chinese Text And Encoding

This project contains Chinese UI strings, comments, file names, and release notes. Treat them as intentional content.

- Read text files as UTF-8 when possible. In PowerShell, prefer `Get-Content -Encoding UTF8` for files that may contain Chinese text.
- If Chinese text appears as mojibake, do not rewrite or "fix" it immediately. Re-read the file with UTF-8 first and confirm the real content.
- Preserve existing Chinese strings in XAML, C# attributes, tooltips, comments, release notes, and model help files unless the requested change explicitly targets that text.
- Avoid broad search-and-replace operations that might touch Chinese text, escaped Unicode, or localized resources.
- When editing files that already contain Chinese text, save them as UTF-8 and verify the edited region still displays correctly before finishing.
- When reporting build or test errors, distinguish real compiler errors from terminal display or code-page rendering issues.

## Release Checklist

Use this checklist when preparing a LabelMinus release.

1. Check the workspace before committing.
   - Run `git status --short` and `git diff --stat`.
   - Confirm the commit contains only the source changes intended for the release.
   - Do not commit temporary artifacts such as `.codex_tmp/`, screenshots, OCR test input/output, publish directories, or other generated release files.

2. Commit and push source code first.
   - Use a clear commit message.
   - Push the current main branch, for example `git push origin master`.
   - Create the version tag only after the branch push succeeds, for example `git tag v1.3`.
   - Push the tag with `git push origin v1.3`.
   - If an old tag is missing locally, fetch only that tag, for example `git fetch origin refs/tags/v1.2:refs/tags/v1.2`.
   - Do not force-overwrite existing tags.

3. Publish a self-contained package.
   - Publish to `D:\out`.
   - Prefer a win-x64 self-contained single-file publish:
     `dotnet publish LabelMinusinWPF\LabelMinusinWPF.csproj -c Release -r win-x64 --self-contained true -o D:\out\LabelMinusinWPF-vX.Y-win-x64-portable /p:Version=X.Y.0 /p:AssemblyVersion=X.Y.0.0 /p:FileVersion=X.Y.0.0 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=none /p:DebugSymbols=false /p:SatelliteResourceLanguages=zh-Hans`
   - After publishing, check that `LabelMinusinWPF.exe` has the expected `FileVersion` and `ProductVersion`.

4. Keep release asset naming consistent.
   - v1.2 used `LabelMinus_ver1.2.zip`.
   - Use `LabelMinus_verX.Y.zip` for later versions, for example `LabelMinus_ver1.3.zip`.
   - Zip the contents of the portable publish directory, not the outer directory itself.

5. Create or update the GitHub Release.
   - Use the release title `LabelMinus vX.Y`.
   - Upload the asset as `LabelMinus_verX.Y.zip`.
   - Release notes should describe user-visible changes.
   - Avoid engineering-only notes such as release packaging, documentation/dependency housekeeping, or test additions unless the user explicitly asks for them.
   - Before writing notes, review the full change set with `git log vOLD..vNEW` and `git diff --stat vOLD..vNEW`.
   - To create a release:
     `gh release create vX.Y D:\out\LabelMinus_verX.Y.zip --repo Yilibala-kid/LabelMinusinWPF --title "LabelMinus vX.Y" --notes "..."`
   - If the release already exists, update it:
     `gh release edit vX.Y --repo Yilibala-kid/LabelMinusinWPF --title "LabelMinus vX.Y" --notes "..."`

6. Verify before finishing.
   - Run `gh release view vX.Y --repo Yilibala-kid/LabelMinusinWPF --json name,tagName,assets,url,body`.
   - Confirm the title, tag, asset name, URL, and notes are correct.
   - Run `git status --short`.
   - Confirm there are no unexpected staged or modified files.
   - It is acceptable to leave untracked temporary directories uncommitted, but mention them in the final response.
