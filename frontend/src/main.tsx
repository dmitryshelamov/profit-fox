import '@/app/styles/index.css';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { AppProviders } from '@/app/providers/AppProviders';

const root = document.getElementById('root');

if (!root) {
  throw new Error('The #root element is missing from index.html.');
}

createRoot(root).render(
  <StrictMode>
    <AppProviders />
  </StrictMode>,
);
