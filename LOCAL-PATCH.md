# Local parser isolation patch

Upstream: OneJS **3.9.14**, commit
`19d9435a904d7021becca12139dfc3adb1fc5759`.
This fork isolates OneJS's bundled parser from Unity's own assembly. Its MIT
license is retained. Consumers can install it as a UPM Git dependency pinned to
a full commit hash; embedding is not required.

## Reason

The bundled `ExCSS.Unity.dll` shares its assembly identity with Unity's own parser.
On Unity 6000.6.7f1 it shadows the newer engine copy, causing USS/TSS/UXML import to
throw `MissingMethodException: IStyleRule.get_NestedRules()`. See
[upstream issue #154](https://github.com/Singtaa/OneJS/issues/154).

## Changes from upstream

- Rename the parser assembly and module to `OneJS.ExCSS` / `OneJS.ExCSS.dll`.
- Rename the DLL's `.meta` file, preserving its GUID and importer configuration.
- Change `Runtime/OneJS.Runtime.asmdef` to reference `OneJS.ExCSS.dll`.
- Include the reproducible patch script in `Tools~/PatchOneJsParser.cs`.

Namespaces and parser method bodies are unchanged. Unity's importer resolves its
own `ExCSS.Unity`; OneJS resolves the isolated parser in Editor and player builds.

Source DLL SHA-256: `3072e203644d4df4fd5f6e495c46179afffe3bb6e66e1f1ebb8de80b17f35dc9`.
Patched DLL SHA-256: `500eadb24de6e05a8318054241478612cd83e26391ca4af22f44acfc1fa07790`.
Method-body fingerprint before and after:
`20cf74d2382e242233f15037f4b004d5c52201aa360c10a6f228e00544515062`.

## Reproducing or updating

To reproduce the binary transformation, embed an unpatched copy of the pinned
upstream version in a Unity project. From that project's root, run the script
from a checkout of this fork (replace `/path/to/OneJS` below):

```sh
unity command --project-path "$PWD" --caller plugin --skill unity-cli --result-only \
  script run -- --file /path/to/OneJS/Tools~/PatchOneJsParser.cs --entry PatchOneJsParser.Run
```

The script uses Unity's installed Mono.Cecil, checks the original DLL hash, and
rejects any change to parser method bodies. It is safe to rerun after patching.
Restart the Editor after replacing an assembly that was already loaded.

An upgrade needs a new upstream base and a review of this patch; the script
rejects unknown parser bytes. Validate runtime USS compilation, Unity USS/TSS/UXML
imports, React UI input and reload, then a standalone player smoke test. Switch
back to upstream when its parser is isolated and those checks pass. Do not edit
`Library/PackageCache`.

Validated by a consuming project on Unity 6000.6.7f1, macOS Apple Silicon:
95 EditMode tests, 10 PlayMode tests, and 13 standalone macOS Mono UI checks
passed. Parser method bodies were fingerprinted before and after the rename.
Unity 7 and IL2CPP have not been validated.
