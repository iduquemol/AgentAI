import { Button } from "@/components/ui/button";

function App() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background">
      <h1 className="text-4xl font-semibold tracking-tight text-foreground">
        Agente Conversacional IA
      </h1>
      <Button>Iniciar conversación</Button>
    </main>
  );
}

export default App;
