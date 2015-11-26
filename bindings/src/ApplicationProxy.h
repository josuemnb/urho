#include "asserts.h"

class ApplicationProxy;

typedef void(*callback_t)(ApplicationProxy *);
typedef int(*sdl_callback)(Urho3D::Context *);

class ApplicationProxy : public Urho3D::Application {
public:
	ApplicationProxy (Urho3D::Context *ctx, callback_t csetup, callback_t cstart, callback_t cstop, const char* args) : Urho3D::Application (ctx)
	{
		setup = csetup;
		start = cstart;
		stop = cstop;
		engineParameters_ = Urho3D::Engine::ParseParameters(Urho3D::ParseArguments(args));
	}

	void Setup ()
	{
		setup (this);
	}

	void Start ()
	{
		start (this);
	}

	void Stop ()
	{
		stop (this);
	}

	Urho3D::Engine *GetEngine ()
	{
		return engine_.Get ();
	}
	
private:
	callback_t setup, start, stop;
};

