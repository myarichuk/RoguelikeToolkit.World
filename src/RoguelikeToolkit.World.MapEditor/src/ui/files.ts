import type { FileService } from '../viewmodels/ViewModel';

/** Browser implementation of FileService: anchor downloads and a hidden file input. */
export const browserFiles: FileService = {
  save(blob, filename) {
    const url = URL.createObjectURL(blob);
    const a = Object.assign(document.createElement('a'), { href: url, download: filename });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  },
  pick(accept) {
    return new Promise((resolve) => {
      const input = Object.assign(document.createElement('input'), { type: 'file', accept });
      input.onchange = () => resolve(input.files?.[0] ?? null);
      input.addEventListener('cancel', () => resolve(null));
      input.click();
    });
  },
};
