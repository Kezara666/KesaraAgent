import { describe, expect, it } from 'vitest';
import { renderMarkdown } from './markdown';

describe('renderMarkdown', () => {
  it('turns Markdown headings and lists into HTML', () => {
    const html = renderMarkdown('### **Writing**\n* **Drafting:** emails');
    expect(html).toContain('<h3>');
    expect(html).toContain('<strong>Writing</strong>');
    expect(html).toContain('<li>');
    expect(html).not.toContain('###');
  });

  it('removes script tags from model output', () => {
    const html = renderMarkdown('hello <script>alert(1)</script> world');
    expect(html).not.toContain('<script');
    expect(html).toContain('hello');
  });
});
