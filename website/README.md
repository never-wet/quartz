# Quartz download website

This folder contains the dependency-free Quartz product and download homepage.

## Preview locally

From the repository root:

```powershell
python -m http.server 8080
```

Then open `http://localhost:8080/website/`.

The download links use `../release/QuartzSetup.exe`. Run `installer/build-installer.ps1` before testing the download. When deploying the website independently, replace all three relative installer links in `index.html` with the final hosted release URL.

No build step or external web dependency is required.
