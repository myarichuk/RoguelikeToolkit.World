let counter = 0;
const session = Math.floor(Math.random() * 0x7fffffff).toString(36);
/** Unique within a document lifetime and across sessions (documents are merged on paste/import). */
export const newId = (prefix: string): string => `${prefix}_${session}_${(counter++).toString(36)}`;
