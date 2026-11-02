/** The `errors` of an API `ValidationProblemDetails`: messages by the key of the request body. */
export type ProblemErrors = Readonly<Record<string, readonly string[]>> | undefined;

/** Server errors laid out for a form: by field, and the ones that match no field. */
export interface FieldErrors {
  readonly fields: Readonly<Record<string, readonly string[]>>;
  readonly form: readonly string[];
}

export function layOutProblems(
  errors: ProblemErrors,
  isField: (key: string) => boolean,
): FieldErrors {
  const fields: Record<string, readonly string[]> = {};
  const form: string[] = [];

  for (const [key, messages] of Object.entries(errors ?? {})) {
    if (isField(key)) {
      fields[key] = messages;
    } else {
      form.push(...messages);
    }
  }

  return { fields, form };
}
