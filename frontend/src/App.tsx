import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Navigation } from './components/Navigation/Navigation';
import { Home } from './pages/Home/Home';
import { Library } from './pages/Library/Library';
import { Bookshelf } from './pages/Bookshelf/Bookshelf';
import { WallpaperGenerator } from './pages/WallpaperGenerator/WallpaperGenerator';
import { AudibleConnect } from './pages/AudibleConnect/AudibleConnect';
import './App.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Navigation />
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/library" element={<Library />} />
          <Route path="/bookshelf" element={<Library />} />
          <Route path="/bookshelf/:id" element={<Bookshelf />} />
          <Route path="/wallpaper" element={<WallpaperGenerator />} />
          <Route path="/connect" element={<AudibleConnect />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}

export default App;
