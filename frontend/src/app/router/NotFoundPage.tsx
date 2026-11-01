import { Link } from '@tanstack/react-router';

export function NotFoundPage() {
  return (
    <section className="space-y-3">
      <h1 className="text-2xl font-bold">Страница не найдена</h1>
      <Link to="/strategies" className="text-accent hover:underline">
        К стратегиям
      </Link>
    </section>
  );
}
