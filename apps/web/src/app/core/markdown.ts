import { marked } from 'marked';
import DOMPurify from 'dompurify';

/**
 * Turns a model reply from Markdown into HTML. Model text is untrusted, so the result is sanitized before it is shown.
 */
export function renderMarkdown(text: string): string {
  const html = marked.parse(text, { async: false, gfm: true, breaks: true });
  return DOMPurify.sanitize(html);
}
