# Quartz download website

This folder contains the dependency-free Quartz product and download homepage.

## Preview locally

From the repository root:

```powershell
python -m http.server 8080
```

Then open `http://localhost:8080/website/`.

The download links use the GitHub Release asset URL for `Quartz-2.0.0-Setup.exe`. Before publishing the website, make sure the `v2.0.0` release is published and includes `Quartz-2.0.0-Setup.exe` as an uploaded asset.

No build step or external web dependency is required.
