import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { VietsubTranslationResourceModal } from './VietsubTranslationResourceModal';

describe('VietsubTranslationResourceModal', () => {
  it('renders an explicit non-blocking resource warning with cancel and continue actions', () => {
    const html = renderToStaticMarkup(createElement(VietsubTranslationResourceModal, {
      alert: {
        errorCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED',
        title: 'Tài nguyên máy thấp hơn mức khuyến nghị',
        message: 'RAM trống hiện chỉ còn 1,17 GB; mức khuyến nghị là 4 GB.',
        action: 'TRANSLATE'
      },
      onDismiss: () => { },
      onContinue: () => { }
    }));

    expect(html).toContain('role="alertdialog"');
    expect(html).toContain('aria-modal="true"');
    expect(html).toContain('confirmation-overlay vietsub-resource-modal-overlay');
    expect(html).toContain('Tài nguyên máy thấp hơn mức khuyến nghị');
    expect(html).toContain('RAM trống hiện chỉ còn 1,17 GB; mức khuyến nghị là 4 GB.');
    expect(html).toContain('VideoMaker sẽ thử nạp');
    expect(html).toContain('Vẫn tiếp tục');
    expect(html).toContain('Hủy');
  });
});
