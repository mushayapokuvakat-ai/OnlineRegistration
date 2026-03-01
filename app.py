import os
from flask import Flask, request, jsonify, send_from_directory, abort
from flask_sqlalchemy import SQLAlchemy
from flask_bcrypt import Bcrypt
from flask_jwt_extended import JWTManager, create_access_token, jwt_required, get_jwt_identity, verify_jwt_in_request, get_jwt
from flask_cors import CORS
from flask_limiter import Limiter
from flask_limiter.util import get_remote_address
from datetime import timedelta, datetime
from functools import wraps
from dotenv import load_dotenv
from models import db, User, Course, Enrollment, Setting

load_dotenv()

app = Flask(__name__, static_folder='wwwroot', static_url_path='')
app.config['SECRET_KEY'] = os.getenv('SECRET_KEY', 'dev-secret')
app.config['SQLALCHEMY_DATABASE_URI'] = os.getenv('DATABASE_URL', 'sqlite:///online_reg.db')
app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False
app.config['JWT_SECRET_KEY'] = os.getenv('JWT_SECRET_KEY', 'jwt-dev-secret')
app.config['JWT_ACCESS_TOKEN_EXPIRES'] = timedelta(hours=1)

db.init_app(app)
bcrypt = Bcrypt(app)
jwt = JWTManager(app)
CORS(app)

# Rate Limiting
limiter = Limiter(
    get_remote_address,
    app=app,
    default_limits=["100 per minute"],
    storage_uri="memory://"
)

# --- Helper Decorators ---
def role_required(role):
    def wrapper(fn):
        @wraps(fn)
        def decorator(*args, **kwargs):
            verify_jwt_in_request()
            claims = get_jwt_identity()
            if claims.get('role') != role:
                return jsonify({"success": False, "message": "Access denied"}), 403
            return fn(*args, **kwargs)
        return decorator
    return wrapper

# --- Fortress & Stealth Security Middleware ---
@app.before_request
def security_shield():
    # 1. Bot Filter
    user_agent = request.headers.get('User-Agent', '').lower()
    illegal_bots = ["curl", "python", "postman", "go-http", "java", "wget", "httpclient"]
    if any(bot in user_agent for bot in illegal_bots):
        return "Automated access is blocked. Please use a standard web browser.", 403

    # 2. Shield Key Check (Fortress Defense)
    # Allows browser users to see the site, but protects the API from unauthorized scripts.
    shield_key = os.getenv('SHIELD_KEY')
    request_key = request.headers.get('X-Shield-Key')
    is_api_request = request.path.startswith('/api')
    is_from_tunnel = 'X-Forwarded-For' in request.headers or 'X-Original-Host' in request.headers
    
    if is_api_request and is_from_tunnel and shield_key and request_key != shield_key:
        return "Security Alert: Connection blocked by Fortress Firewall. Secret Handshake missing.", 403

    # 3. IP Whitelist
    enable_whitelist = os.getenv('ENABLE_IP_WHITELIST', 'false').lower() == 'true'
    if enable_whitelist:
        allowed_ips = os.getenv('ALLOWED_IPS', '127.0.0.1,::1,41.220.16.98').split(',')
        client_ip = request.headers.get('X-Forwarded-For', request.remote_addr)
        if client_ip not in allowed_ips:
            return f"Nuclear Lockdown: Access denied for IP {client_ip}.", 403

# --- Static File Serving ---
@app.route('/')
def serve_index():
    return send_from_directory('wwwroot', 'index.html')

@app.route('/<path:path>')
def serve_static(path):
    return send_from_directory('wwwroot', path)

# --- Auth Routes ---
@app.route('/api/auth/register', methods=['POST'])
def register():
    data = request.get_json()
    if User.query.filter_by(email=data['email']).first():
        return jsonify({"success": False, "message": "Email already exists"}), 400
    
    hashed_password = bcrypt.generate_password_hash(data['password']).decode('utf-8')
    new_user = User(
        full_name=data['fullName'],
        email=data['email'],
        password_hash=hashed_password,
        course=data.get('course', 'N/A'),
        role='Student'
    )
    db.session.add(new_user)
    db.session.commit()
    return jsonify({"success": True, "message": "Registration successful"}), 201

@app.route('/api/auth/login', methods=['POST'])
def login():
    data = request.get_json()
    user = User.query.filter_by(email=data['email']).first()
    if user and bcrypt.check_password_hash(user.password_hash, data['password']):
        access_token = create_access_token(identity={'id': user.user_id, 'role': user.role})
        return jsonify({
            "success": True,
            "data": {
                "token": access_token,
                "fullName": user.full_name,
                "email": user.email,
                "role": user.role
            }
        })
    return jsonify({"success": False, "message": "Invalid email or password"}), 401

# --- Course & Enrollment Routes (Student) ---
@app.route('/api/courses', methods=['GET'])
def get_courses():
    courses = Course.query.filter_by(is_active=True).all()
    return jsonify({"success": True, "data": [c.to_dict() for c in courses], "message": f"Found {len(courses)} course(s)."})

@app.route('/api/courses/my-courses', methods=['GET'])
@role_required('Student')
def get_my_courses():
    student_id = get_jwt_identity()['id']
    enrollments = Enrollment.query.filter_by(student_id=student_id).all()
    return jsonify({"success": True, "data": [e.to_dict() for e in enrollments], "message": f"You are enrolled in {len(enrollments)} course(s)."})

@app.route('/api/courses/enroll', methods=['POST'])
@role_required('Student')
def enroll():
    student_id = get_jwt_identity()['id']
    data = request.get_json()
    course_ids = data.get('courseIds', [])
    
    # Check registration settings
    reg_open = Setting.query.get('RegistrationOpen')
    if reg_open and reg_open.value.lower() != 'true':
        return jsonify({"success": False, "message": "Registration is currently closed."}), 400
    
    max_courses_setting = Setting.query.get('MaxCoursesPerStudent')
    max_courses = int(max_courses_setting.value) if max_courses_setting else 5
    
    current_count = Enrollment.query.filter_by(student_id=student_id).count()
    if current_count + len(course_ids) > max_courses:
        return jsonify({"success": False, "message": f"You cannot register for more than {max_courses} courses."}), 400

    enrolled_count = 0
    user = User.query.get(student_id)

    for cid in course_ids:
        course = Course.query.get(cid)
        if course and course.is_active and course.enrolled_count < course.max_capacity:
            existing = Enrollment.query.filter_by(student_id=student_id, course_id=cid).first()
            if not existing:
                enrollment = Enrollment(
                    student_id=student_id,
                    student_name=user.full_name,
                    student_email=user.email,
                    course_id=course.course_id,
                    course_name=course.course_name,
                    course_code=course.course_code
                )
                course.enrolled_count += 1
                db.session.add(enrollment)
                enrolled_count += 1

    db.session.commit()
    return jsonify({"success": True, "data": {"enrolledCourses": enrolled_count}, "message": f"Successfully enrolled in {enrolled_count} course(s)."})

@app.route('/api/courses/enroll/<int:course_id>', methods=['DELETE'])
@role_required('Student')
def drop_course(course_id):
    student_id = get_jwt_identity()['id']
    enrollment = Enrollment.query.filter_by(student_id=student_id, course_id=course_id).first()
    if enrollment:
        course = Course.query.get(course_id)
        if course:
            course.enrolled_count -= 1
        db.session.delete(enrollment)
        db.session.commit()
        return jsonify({"success": True, "message": "Course dropped successfully."})
    return jsonify({"success": False, "message": "You are not enrolled in this course."}), 404

@app.route('/api/courses/settings', methods=['GET'])
def get_settings_public():
    settings = {s.key: s.value for s in Setting.query.all()}
    return jsonify({"success": True, "data": settings})

# --- Lecturer Routes ---
@app.route('/api/lecturer/courses', methods=['GET'])
@role_required('Lecturer')
def get_lecturer_courses():
    lecturer_id = get_jwt_identity()['id']
    courses = Course.query.filter_by(lecturer_id=lecturer_id).all()
    return jsonify({"success": True, "data": [c.to_dict() for c in courses]})

@app.route('/api/lecturer/courses/<int:id>/students', methods=['GET'])
@role_required('Lecturer')
def get_course_students(id):
    lecturer_id = get_jwt_identity()['id']
    course = Course.query.get(id)
    if not course or course.lecturer_id != lecturer_id:
        return jsonify({"success": False, "message": "Access denied"}), 403
    
    enrollments = Enrollment.query.filter_by(course_id=id).all()
    students = [{
        "userID": e.student_id,
        "fullName": e.student_name,
        "email": e.student_email,
        "enrolledAt": e.enrolled_at.isoformat()
    } for e in enrollments]
    
    return jsonify({
        "success": True, 
        "data": {
            "course": {"courseID": course.course_id, "courseName": course.course_name, "courseCode": course.course_code},
            "students": students,
            "totalStudents": len(students)
        }
    })

# --- Admin Routes ---
@app.route('/api/admin/users', methods=['GET'])
@role_required('Admin')
def get_all_users():
    users = User.query.order_by(User.created_at.desc()).all()
    return jsonify({"success": True, "data": [u.to_dict() for u in users]})

@app.route('/api/admin/users/<int:id>', methods=['DELETE'])
@role_required('Admin')
def delete_user(id):
    user = User.query.get(id)
    if user:
        if user.role == 'Admin':
            return jsonify({"success": False, "message": "Cannot delete admin"}), 400
        db.session.delete(user)
        db.session.commit()
        return jsonify({"success": True, "message": "User deleted successfully."})
    return jsonify({"success": False, "message": "User not found"}), 404

@app.route('/api/admin/courses', methods=['GET'])
@role_required('Admin')
def get_all_courses_admin():
    courses = Course.query.all()
    return jsonify({"success": True, "data": [c.to_dict() for c in courses]})

@app.route('/api/admin/courses', methods=['POST'])
@role_required('Admin')
def add_course():
    data = request.get_json()
    new_course = Course(
        course_name=data['courseName'],
        course_code=data['courseCode'],
        description=data.get('description', ''),
        lecturer_id=data.get('lecturerID'),
        max_capacity=data.get('maxCapacity', 100)
    )
    db.session.add(new_course)
    db.session.commit()
    return jsonify({"success": True, "data": {"courseId": new_course.course_id}, "message": "Course added successfully."})

@app.route('/api/admin/courses/<int:id>', methods=['DELETE'])
@role_required('Admin')
def delete_course(id):
    course = Course.query.get(id)
    if course:
        course.is_active = False
        db.session.commit()
        return jsonify({"success": True, "message": "Course removed successfully."})
    return jsonify({"success": False, "message": "Course not found"}), 404

@app.route('/api/admin/settings', methods=['GET'])
@role_required('Admin')
def get_settings():
    settings = {s.key: s.value for s in Setting.query.all()}
    return jsonify({"success": True, "data": settings})

@app.route('/api/admin/settings', methods=['PUT'])
@role_required('Admin')
def update_settings():
    data = request.get_json()
    for key, value in data.items():
        setting = Setting.query.get(key)
        if setting:
            setting.value = str(value).lower() if isinstance(value, bool) else str(value)
        else:
            db.session.add(Setting(key=key, value=str(value)))
    db.session.commit()
    return jsonify({"success": True, "message": "Settings updated successfully."})

# --- Database Seeding ---
def seed_db():
    with app.app_context():
        db.create_all()
        # Admin
        admin_email = "patnat@system.com"
        if not User.query.filter_by(email=admin_email).first():
            admin_hash = bcrypt.generate_password_hash("Pyruvate#13").decode('utf-8')
            admin = User(full_name='Patnat', email=admin_email, password_hash=admin_hash, role='Admin')
            db.session.add(admin)
        
        # Lecturers
        lecturers = [
            ("Prof. Marie Dupont", "marie.dupont@university.com"),
            ("Dr. Alan Turing Jr.", "alan.turing@university.com"),
            ("Prof. Grace Hopper", "grace.hopper@university.com")
        ]
        lec_ids = {}
        for name, email in lecturers:
            u = User.query.filter_by(email=email).first()
            if not u:
                hash = bcrypt.generate_password_hash("Lecturer@123").decode('utf-8')
                u = User(full_name=name, email=email, password_hash=hash, role='Lecturer')
                db.session.add(u)
                db.session.flush()
            lec_ids[email] = u.user_id

        # Courses
        if not Course.query.first():
            courses = [
                ("French", "FRN101", "Learn French fundamentals.", "marie.dupont@university.com"),
                ("Calculus", "MAT201", "Limits and series.", "alan.turing@university.com"),
                ("Theory of Computing", "CSC301", "Complexity theory.", "grace.hopper@university.com")
            ]
            for name, code, desc, lemail in courses:
                c = Course(course_name=name, course_code=code, description=desc, lecturer_id=lec_ids.get(lemail), lecturer_name=name)
                db.session.add(c)
        
        # Settings
        if not Setting.query.get('RegistrationOpen'):
            db.session.add(Setting(key='RegistrationOpen', value='true'))
            db.session.add(Setting(key='MaxCoursesPerStudent', value='5'))

        db.session.commit()

if __name__ == '__main__':
    seed_db()
    # Run on port 9091
    app.run(host='0.0.0.0', port=9091, debug=True)
