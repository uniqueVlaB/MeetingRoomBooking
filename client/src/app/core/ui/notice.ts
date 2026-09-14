/** A message shown to the user about the last thing they did. */
export interface Notice {
  kind: 'info' | 'error';
  text: string;
}

/** Builds an informational notice. */
export function info(text: string): Notice {
  return { kind: 'info', text };
}

/** Builds an error notice. */
export function failure(text: string): Notice {
  return { kind: 'error', text };
}
