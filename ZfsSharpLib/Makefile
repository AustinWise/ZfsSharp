
native_helpers.o: native_helpers.cc

native_helpers.so: native_helpers.o
	$(CXX) -fPIC -shared -o $@ $^

native_helpers.dylib: native_helpers.o
	$(CXX) -shared -o $@ $^
