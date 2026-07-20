document.querySelectorAll('a[href^="#"]').forEach((link) => {
  link.addEventListener('click', (event) => {
    const target = document.querySelector(link.getAttribute('href'));
    if (!target) return;
    event.preventDefault();
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
  });
});

const versionOptions = document.querySelectorAll('.version-option');
const versionPanels = document.querySelectorAll('[data-version-panel]');

function selectVersion(button, scrollIntoView = false) {
  const version = button.dataset.version;
  versionOptions.forEach((option) => {
    option.classList.toggle('active', option === button);
  });
  versionPanels.forEach((panel) => {
    panel.classList.toggle('active', panel.dataset.versionPanel === version);
  });
  if (scrollIntoView) {
    button.scrollIntoView({ block: 'nearest' });
  }
}

versionOptions.forEach((button) => {
  button.addEventListener('click', () => selectVersion(button));
});

const currentVersion = document.querySelector('[data-current-version="true"]');
if (currentVersion) {
  selectVersion(currentVersion, true);
}
